using EventStore.Domain.EventLog;
using EventStore.Domain.Replication;
using EventStore.Inbox;
using EventStore.LeaderElection;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStore.Replication;

// ADR-033 -- gossip/full-mesh: every tick, push whatever this site has
// appended since each known peer's own PeerSyncCursor.LastAckedSequenceNumber.
// A push failure (peer unreachable) just leaves the cursor where it was --
// "nothing queued is lost, sync just falls behind" (this ADR's own text) --
// there is no separate physical outbox table; the durable Events table
// plus PeerSyncCursor together already are the fault/abend/restart-
// tolerant outbox this ADR requires. Merkle-tree catch-up (this ADR's own
// named efficiency optimization for a long disconnection) is NOT built at
// this stage -- every tick resends everything since the last ack, which
// is correct, just not as efficient as a hash-tree range diff would be
// for a long-disconnected peer; flagged in 08-build-plan.md, not silently
// dropped.
public class PeerSyncWorker(
    IServiceScopeFactory scopeFactory, PeerAddressBook addressBook, ILogger<PeerSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    // ADR-078 -- one of the 4 named worker roles, independent of "Router"'s
    // own lease; either can be held/lost without affecting the other.
    private const string WorkerRole = "PeerSyncOutboxPump";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(5);
    // See RouterWorker's own identical field for why this isn't renewed on
    // every tick.
    private static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(2.5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isLeader = false;
        var nextRenewalAt = DateTimeOffset.MinValue; // forces an immediate first acquisition attempt
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EventStoreContext>();

                if (DateTimeOffset.UtcNow >= nextRenewalAt)
                {
                    var leaderElection = scope.ServiceProvider.GetRequiredService<LeaderElectionService>();
                    var acquired = await leaderElection.TryAcquireOrRenewAsync(WorkerRole, LeaseHolderId.Current, LeaseDuration, stoppingToken);
                    if (acquired != isLeader)
                    {
                        isLeader = acquired;
                        logger.LogInformation("Peer sync {State} the {WorkerRole} lease", isLeader ? "acquired" : "lost", WorkerRole);
                    }
                    nextRenewalAt = isLeader ? DateTimeOffset.UtcNow + RenewInterval : DateTimeOffset.MinValue;
                }

                if (isLeader)
                {
                    var client = scope.ServiceProvider.GetRequiredService<PeerSyncClient>();
                    var originIdOptions = scope.ServiceProvider.GetRequiredService<IOptions<OriginIdOptions>>();
                    var syncOptions = scope.ServiceProvider.GetRequiredService<IOptions<PeerSyncOptions>>();
                    var residencyPolicies = scope.ServiceProvider.GetRequiredService<AppResidencyPolicyService>();
                    await RunOnceAsync(db, client, addressBook, originIdOptions.Value.OriginId, syncOptions.Value.BatchSize, residencyPolicies, logger, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Peer sync tick failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    public static async Task RunOnceAsync(
        EventStoreContext db, PeerSyncClient client, PeerAddressBook addressBook, string selfOriginId, int batchSize,
        AppResidencyPolicyService? residencyPolicies = null, ILogger? logger = null, CancellationToken ct = default)
    {
        // ADR-061 -- loaded once per tick, not once per peer: a small,
        // AppId-keyed table, no reason to re-query it once per address.
        var policies = residencyPolicies is null
            ? new Dictionary<string, List<string>>()
            : await residencyPolicies.GetAllPoliciesAsync(ct);

        foreach (var address in addressBook.KnownAddresses)
        {
            try
            {
                await SyncOnceWithAsync(db, client, addressBook, address, selfOriginId, batchSize, policies, ct);
            }
            catch (Exception)
            {
                // ADR-033 -- a single unreachable peer must never block sync
                // with every OTHER peer this tick; its own cursor simply
                // doesn't advance, exactly the "falls behind, nothing lost"
                // posture this ADR names.
            }
        }

        if (logger is not null)
            WarnIfResidencyUnderReplicated(addressBook, policies, logger);
    }

    // ADR-061's own honest, named tension with ADR-033's 2-replica
    // minimum: a region configured with only one live site is surfaced as
    // an operational signal (a log line here; a real deployment would wire
    // this to a metric), never a hard failure or a blocked write --
    // residency still wins, the deployment carries the responsibility to
    // ensure enough live sites exist per region a tenant might restrict to.
    private static void WarnIfResidencyUnderReplicated(
        PeerAddressBook addressBook, IReadOnlyDictionary<string, List<string>> residencyPolicies, ILogger logger)
    {
        var knownRegions = addressBook.KnownAddresses.Select(addressBook.RegionFor).Where(r => r is not null).ToList();
        foreach (var (appId, allowedRegions) in residencyPolicies)
        {
            if (allowedRegions.Count == 0)
                continue;

            var liveSitesInAllowedRegions = knownRegions.Count(r => allowedRegions.Contains(r!));
            if (liveSitesInAllowedRegions < 2)
                logger.LogWarning(
                    "AppId {AppId}'s residency constraint {AllowedRegions} is satisfied by only {LiveSites} known live site(s) -- ADR-033's 2-replica minimum cannot be met without knowingly accepting single-site risk for this tenant",
                    appId, allowedRegions, liveSitesInAllowedRegions);
        }
    }

    private static async Task SyncOnceWithAsync(
        EventStoreContext db, PeerSyncClient client, PeerAddressBook addressBook, string address, string selfOriginId, int batchSize,
        IReadOnlyDictionary<string, List<string>> residencyPolicies, CancellationToken ct)
    {
        var peerId = addressBook.PeerIdFor(address);
        if (peerId is null)
        {
            var (whoAmIPeerId, region) = await client.WhoAmIAsync(address, ct);
            peerId = whoAmIPeerId;
            addressBook.SetPeerIdAndRegion(address, peerId, region);
        }

        if (peerId == selfOriginId)
            return; // never sync with ourselves, e.g. a seed address that happens to resolve back to this site

        var cursor = await db.PeerSyncCursors.SingleOrDefaultAsync(c => c.PeerId == peerId, ct);
        var isNewCursor = cursor is null;
        cursor ??= new PeerSyncCursor { PeerId = peerId };

        var candidates = await db.Events
            .AsNoTracking()
            .Where(e => e.SequenceNumber > cursor.LastAckedSequenceNumber)
            .OrderBy(e => e.SequenceNumber)
            .Take(batchSize)
            .ToListAsync(ct);

        // ADR-061 -- enforced HERE, at the peer-sync outbox, per-EVENT, not
        // wholesale per-peer: an unconstrained AppId's events (no row in
        // residencyPolicies, or an empty AllowedRegions) are unaffected; a
        // constrained AppId's event is included only if this peer's own
        // tagged Region (learned via whoami/gossip) is in that list. A
        // peer with NO known region can never receive a constrained AppId's
        // events at all -- the conservative default this ADR's own
        // "residency wins" priority implies for an unconfirmed destination.
        // Skipped events still count toward the cursor advancing below --
        // they are permanently excluded from THIS peer, never retried.
        var peerRegion = addressBook.RegionFor(address);
        var pending = candidates.Where(e =>
            !residencyPolicies.TryGetValue(e.AppId, out var allowedRegions) || allowedRegions.Count == 0
                || (peerRegion is not null && allowedRegions.Contains(peerRegion)))
            .ToList();

        cursor.LastSyncAttemptAt = DateTimeOffset.UtcNow;

        var payloads = pending.Select(ToPayload).ToList();
        var request = new PeerSyncPushRequest(selfOriginId, payloads, addressBook.KnownPeers().ToList());
        var response = await client.PushAsync(address, request, ct);

        addressBook.Merge(response.KnownPeers);

        // Advances past the full CANDIDATE window, not just what was
        // actually sent -- a residency-skipped event is permanently
        // excluded from this specific peer, never retried on a later tick
        // (ADR-061).
        if (candidates.Count > 0)
            cursor.LastAckedSequenceNumber = candidates[^1].SequenceNumber;
        cursor.LastSyncSuccessAt = DateTimeOffset.UtcNow;

        if (isNewCursor)
            db.PeerSyncCursors.Add(cursor);
        await db.SaveChangesAsync(ct);
    }

    // Exposed for tests exercising PeerSyncReceiver directly, without a
    // full RunOnceAsync/HTTP round trip -- the same "public testable seam"
    // shape RouterWorker/ChannelDerivationWorker's own static RunOnceAsync
    // methods already provide.
    public static ReplicatedEventPayload ToPayload(StoredEvent e) => new(
        e.SequenceNumber, e.EventId, e.AppId, e.EventType, e.SchemaVersion, e.Payload, e.PayloadHash,
        e.OccurredAt, e.ActorId, e.OriginId ?? "unknown", e.LogicalClock ?? "", e.ExpectedVersion, null,
        e.AuthorityStatus, e.AttestedActorId, e.AttestedClaims);
}
