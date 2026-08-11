using System.Security.Claims;
using EventStore.Inbox;
using EventStore.LeaderElection;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
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
        .AddScoped<AppResidencyPolicyService>()
        .AddHostedService<PeerSyncWorker>();

    public static WebApplication MapPeerSyncEndpoints(this WebApplication app)
    {
        // ADR-051 -- the handshake a newly-contacted seed answers with its
        // own identity (== its own OriginId, docs/features/replication-and-
        // sharding.md's ER diagram: "OriginId = PeerId"). ADR-061's own
        // Region tag rides along on this SAME existing handshake -- not a
        // new discovery mechanism, just one more field on an answer this
        // site already gives.
        app.MapGet("/peer-sync/whoami", (IOptions<OriginIdOptions> originIdOptions, IOptions<RegionOptions> regionOptions) =>
            Results.Ok(new { originId = originIdOptions.Value.OriginId, region = regionOptions.Value.Region }))
            .RequireAuthorization("peer:sync");

        app.MapPost("/peer-sync/push", async (
            PeerSyncPushRequest request, EventStoreContext db, PeerAddressBook addressBook, CancellationToken ct) =>
            Results.Ok(await PeerSyncReceiver.ReceiveAsync(db, request, addressBook, ct)))
            .RequireAuthorization("peer:sync");

        // ADR-061 -- reuses registry:admin, the same "admin tier" scope
        // FeatureFlagEndpoints already gates its own narrow per-AppId
        // configuration write with, rather than inventing a dedicated scope
        // for a mechanism this narrow.
        app.MapPut("/replication/residency/{appId}", async (
            string appId, SetAllowedRegionsRequest request, ClaimsPrincipal user, AppResidencyPolicyService residencyPolicies, CancellationToken ct) =>
        {
            if (!AppIdScopeEvaluator.CanAdminister(user, appId))
                return Results.Forbid();

            var result = await residencyPolicies.SetAllowedRegionsAsync(appId, request.AllowedRegions, user, ct);
            return result switch
            {
                PublishResult.Accepted a => Results.Ok(new { sequenceNumber = a.SequenceNumber }),
                PublishResult.UnregisteredEventType => Results.Problem(statusCode: 500, detail: "the reserved event type was not registered before publishing -- this is an EnsureRegisteredAsync bug, not a caller error"),
                PublishResult.Forbidden => Results.Forbid(),
                _ => Results.Problem(statusCode: 500),
            };
        }).RequireAuthorization("registry:admin");

        return app;
    }
}

public record SetAllowedRegionsRequest(List<string> AllowedRegions);
