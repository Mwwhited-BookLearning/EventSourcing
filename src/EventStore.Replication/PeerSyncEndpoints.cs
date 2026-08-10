using EventStore.Inbox;
using EventStore.LeaderElection;
using EventStore.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EventStore.Replication;

public static class PeerSyncEndpoints
{
    public static IServiceCollection AddReplication(this IServiceCollection services) => services
        .AddLeaderElection()
        .AddSingleton<PeerAddressBook>()
        .AddScoped<PeerSyncClient>()
        .AddHostedService<PeerSyncWorker>();

    public static WebApplication MapPeerSyncEndpoints(this WebApplication app)
    {
        // ADR-051 -- the handshake a newly-contacted seed answers with its
        // own identity (== its own OriginId, docs/features/replication-and-
        // sharding.md's ER diagram: "OriginId = PeerId").
        app.MapGet("/peer-sync/whoami", (IOptions<OriginIdOptions> originIdOptions) =>
            Results.Ok(new { originId = originIdOptions.Value.OriginId }))
            .RequireAuthorization("peer:sync");

        app.MapPost("/peer-sync/push", async (
            PeerSyncPushRequest request, EventStoreContext db, PeerAddressBook addressBook, CancellationToken ct) =>
            Results.Ok(await PeerSyncReceiver.ReceiveAsync(db, request, addressBook, ct)))
            .RequireAuthorization("peer:sync");

        return app;
    }
}
