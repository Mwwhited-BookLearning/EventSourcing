using EventStore.Domain.EntityStore;
using EventStore.LeaderElection;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Leader Election via Database-Backed Lease"
// (docs/08-build-plan.md, ADR-078). Exercises LeaderElectionService
// directly against a provider-backed EventStoreContext -- the mechanism
// itself (a compare-and-swap over LeaderLease rows) is what needs proving
// safe under a real database, not the RouterWorker/PeerSyncWorker
// ExecuteAsync loops that consume it (those are ordinary, already-covered
// BackgroundService polling loops; this item's own new risk is entirely in
// the lease arithmetic).
internal static class LeaderElectionScenarioAssertions
{
    public static async Task FirstAcquireForANewRoleCreatesTheLeaseRowAndSucceeds(LeaderElectionService leaderElection)
    {
        var acquired = await leaderElection.TryAcquireOrRenewAsync("leader-demo-role-1", "holder-a", TimeSpan.FromSeconds(30));
        Assert.IsTrue(acquired);
    }

    public static async Task ASecondDifferentHolderCannotAcquireAStillValidLease(LeaderElectionService leaderElection)
    {
        const string role = "leader-demo-role-2";
        Assert.IsTrue(await leaderElection.TryAcquireOrRenewAsync(role, "holder-a", TimeSpan.FromSeconds(30)));
        Assert.IsFalse(await leaderElection.TryAcquireOrRenewAsync(role, "holder-b", TimeSpan.FromSeconds(30)),
            "a still-valid lease held by a different holder must not be acquirable");
    }

    public static async Task TheOriginalHolderCanRenewItsOwnStillValidLease(LeaderElectionService leaderElection)
    {
        const string role = "leader-demo-role-3";
        Assert.IsTrue(await leaderElection.TryAcquireOrRenewAsync(role, "holder-a", TimeSpan.FromSeconds(30)));
        // Renewing (the SAME holder, calling again before expiry) must
        // succeed -- this is the ordinary, expected steady-state case, not
        // a race.
        Assert.IsTrue(await leaderElection.TryAcquireOrRenewAsync(role, "holder-a", TimeSpan.FromSeconds(30)));
    }

    public static async Task AnotherInstanceCanClaimTheLeaseOnceItExpires(LeaderElectionService leaderElection)
    {
        const string role = "leader-demo-role-4";
        Assert.IsTrue(await leaderElection.TryAcquireOrRenewAsync(role, "holder-a", TimeSpan.FromMilliseconds(50)));
        Assert.IsFalse(await leaderElection.TryAcquireOrRenewAsync(role, "holder-b", TimeSpan.FromSeconds(30)),
            "still valid immediately after holder-a's own acquire");

        await Task.Delay(TimeSpan.FromMilliseconds(200)); // past holder-a's own 50ms lease

        Assert.IsTrue(await leaderElection.TryAcquireOrRenewAsync(role, "holder-b", TimeSpan.FromSeconds(30)),
            "a holder that fails to renew in time must let another instance claim the lease");
        // holder-a can no longer renew what it no longer holds.
        Assert.IsFalse(await leaderElection.TryAcquireOrRenewAsync(role, "holder-a", TimeSpan.FromSeconds(30)));
    }

    public static async Task TwoDifferentWorkerRolesHoldAndLoseTheirLeasesCompletelyIndependently(LeaderElectionService leaderElection)
    {
        const string roleX = "leader-demo-role-5x";
        const string roleY = "leader-demo-role-5y";

        Assert.IsTrue(await leaderElection.TryAcquireOrRenewAsync(roleX, "holder-a", TimeSpan.FromMilliseconds(50)));
        Assert.IsTrue(await leaderElection.TryAcquireOrRenewAsync(roleY, "holder-b", TimeSpan.FromSeconds(30)),
            "a completely different role must be independently acquirable by a different holder, no cross-role interaction");

        await Task.Delay(TimeSpan.FromMilliseconds(200)); // past roleX's own 50ms lease, roleY's own 30s lease unaffected

        Assert.IsTrue(await leaderElection.TryAcquireOrRenewAsync(roleX, "holder-c", TimeSpan.FromSeconds(30)),
            "roleX's own expiry must not affect roleY's own still-valid lease");
        Assert.IsFalse(await leaderElection.TryAcquireOrRenewAsync(roleY, "holder-c", TimeSpan.FromSeconds(30)),
            "roleY is still validly held by holder-b, unaffected by roleX's own handover");
    }

    // ADR-078's own exit criterion: proving that having NO mutual-exclusion
    // mechanism at all (which is exactly what ADR-024's ExpectedVersion
    // ALONE provides -- it's advisory, "a stale ExpectedVersion never
    // blocks the write, it only flags the later-applied event," per
    // RouterWorker.FoldAsync's own comment) genuinely allows two
    // uncoordinated fold "workers" to silently lose one of two updates to
    // the SAME entity -- demonstrating this item's own lease mechanism is
    // doing real, independent work, not re-deriving ADR-024. Modeled
    // directly against EntityStoreRow (the actual fold target), not a real
    // multi-threaded race against RouterWorker itself -- a genuine race
    // would be flaky to assert on; this reproduces the identical hazard
    // (two independent contexts, each holding a stale read, both saving)
    // deterministically.
    public static async Task TwoUncoordinatedContextsWithNoMutualExclusionCanSilentlyLoseOneUpdate(Func<EventStoreContext> createContext)
    {
        const string entityId = "leader-demo-double-apply:order:order-1";

        await using (var setup = createContext())
        {
            setup.EntityStore.Add(new EntityStoreRow
            {
                EntityId = entityId, EntityType = "order", ShardKey = "order",
                Version = 0, Data = "{}", Extensions = "{}", Hash = "", LastAppliedLogicalTime = DateTimeOffset.MinValue,
            });
            await setup.SaveChangesAsync();
        }

        // Two independent "workers" -- their own EventStoreContext, their
        // own tracked copy -- both read the SAME row BEFORE either saves,
        // simulating two uncoordinated fold processes racing with no
        // leader election running at all.
        await using var workerA = createContext();
        await using var workerB = createContext();
        var rowA = await workerA.EntityStore.SingleAsync(r => r.EntityId == entityId);
        var rowB = await workerB.EntityStore.SingleAsync(r => r.EntityId == entityId);

        // Worker A folds an event contributing field "a" and saves first.
        rowA.Data = """{"a":1}""";
        rowA.Version = 1;
        await workerA.SaveChangesAsync();

        // Worker B, still holding its OWN stale read (Data "{}",  Version 0),
        // folds a DIFFERENT event contributing field "b" -- unaware A
        // already saved -- and saves too.
        rowB.Data = """{"b":2}""";
        rowB.Version = 1;
        await workerB.SaveChangesAsync();

        await using var verify = createContext();
        var finalRow = await verify.EntityStore.SingleAsync(r => r.EntityId == entityId);
        // Worker A's own contribution ("a") is silently gone -- B's stale
        // read simply overwrote it. ADR-024's ExpectedVersion/ConflictFlag
        // never entered into this at all (neither worker even declared
        // one), which is exactly the point: nothing about that mechanism
        // would have prevented this, because it was never designed to.
        Assert.IsFalse(finalRow.Data.Contains('a'), $"worker A's own update should have been lost with no mutual exclusion in place, but Data was {finalRow.Data}");
        Assert.IsTrue(finalRow.Data.Contains('b'));
    }
}
