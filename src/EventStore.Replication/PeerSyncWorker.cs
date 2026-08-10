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
                    await RunOnceAsync(db, client, addressBook, originIdOptions.Value.OriginId, syncOptions.Value.BatchSize, stoppingToken);
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
        EventStoreContext db, PeerSyncClient client, PeerAddressBook addressBook, string selfOriginId, int batchSize, CancellationToken ct = default)
    {
        foreach (var address in addressBook.KnownAddresses)
        {
            try
            {
                await SyncOnceWithAsync(db, client, addressBook, address, selfOriginId, batchSize, ct);
            }
            catch (Exception)
            {
                // ADR-033 -- a single unreachable peer must never block sync
                // with every OTHER peer this tick; its own cursor simply
                // doesn't advance, exactly the "falls behind, nothing lost"
                // posture this ADR names.
            }
        }
    }

    private static async Task SyncOnceWithAsync(
        EventStoreContext db, PeerSyncClient client, PeerAddressBook addressBook, string address, string selfOriginId, int batchSize, CancellationToken ct)
    {
        var peerId = addressBook.PeerIdFor(address);
        if (peerId is null)
        {
            peerId = await client.WhoAmIAsync(address, ct);
            addressBook.SetPeerId(address, peerId);
        }

        if (peerId == selfOriginId)
            return; // never sync with ourselves, e.g. a seed address that happens to resolve back to this site

        var cursor = await db.PeerSyncCursors.SingleOrDefaultAsync(c => c.PeerId == peerId, ct);
        var isNewCursor = cursor is null;
        cursor ??= new PeerSyncCursor { PeerId = peerId };

        var pending = await db.Events
            .AsNoTracking()
            .Where(e => e.SequenceNumber > cursor.LastAckedSequenceNumber)
            .OrderBy(e => e.SequenceNumber)
            .Take(batchSize)
            .ToListAsync(ct);

        cursor.LastSyncAttemptAt = DateTimeOffset.UtcNow;

        var payloads = pending.Select(ToPayload).ToList();
        var request = new PeerSyncPushRequest(selfOriginId, payloads, addressBook.KnownPeers().ToList());
        var response = await client.PushAsync(address, request, ct);

        addressBook.Merge(response.KnownPeers);

        if (pending.Count > 0)
            cursor.LastAckedSequenceNumber = pending[^1].SequenceNumber;
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
