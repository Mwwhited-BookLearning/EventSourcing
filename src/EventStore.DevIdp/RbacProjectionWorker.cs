using EventStore.Projections.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStore.DevIdp;

// ADR-067 -- the DevIdp half of "Fold into DevIdp via Follow": the Host's
// new EventStore.Rbac write path (RbacEndpoints.cs) is the only place
// RoleGranted/RoleRevoked/PermissionGranted/AppTrustRootRegistered are ever
// published; DevIdp keeps its own existing local tables (Role/RoleAssignment/
// UserPermission/AppTrustRoot, unchanged shape) but populates them here, by
// folding this event stream via RoleService/TrustRootService's own already-
// idempotent methods -- reused verbatim as the fold logic, only the CALLER
// changes (a Follow consumer, not an inbound HTTP request). /connect/token
// still reads these same local tables synchronously, unchanged -- this
// worker is the only new moving part.
//
// Deliberately no persisted checkpoint (docs/08-build-plan.md item 30's own
// "no checkpointing" note): every reconnect (including this worker's own
// startup) always replays from SequenceNumber 0. Accepted as a simple,
// low-risk choice for a dev/POC, low-volume administrative event stream --
// RoleService/TrustRootService's own fold methods are already no-ops on a
// repeat, so a full replay is never wrong, only occasionally redundant.
public class RbacProjectionWorker(
    IServiceScopeFactory scopeFactory,
    FollowClient followClient,
    IOptions<RbacProjectionOptions> options,
    ILogger<RbacProjectionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    // ADR-067 -- exactly the 4 RBAC reserved event types (SchemaRegistered,
    // the 5th, has no DevIdp-side fold target -- EventTypeDefinition stays a
    // directly-written table, per this build stage's own scoping decision).
    // Literal names, not a reference to EventStore.Rbac's own *EventType.Name
    // consts: DevIdp has deliberately never depended on the core engine
    // (EventStoreContext/Persistence/SchemaRegistry) at all, only on Dpop/
    // TicketExchange/Ucan -- referencing EventStore.Rbac here would drag that
    // entire chain in for 4 string constants. Same "duplication over
    // reference" precedent as SchemaRegistryService.cs's own hardcoded
    // "local" OriginId literal.
    private const string RoleGranted = "RoleGranted";
    private const string RoleRevoked = "RoleRevoked";
    private const string PermissionGranted = "PermissionGranted";
    private const string AppTrustRootRegistered = "AppTrustRootRegistered";
    private static readonly string[] EventTypes = [RoleGranted, RoleRevoked, PermissionGranted, AppTrustRootRegistered];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // BackgroundService.StartAsync calls ExecuteAsync directly (not via
        // Task.Run), and only returns once this method reaches its first
        // real suspension point -- everything before that runs
        // SYNCHRONOUSLY, INLINE, on whatever thread is currently inside
        // IHost.StartAsync(). In a WebApplicationFactory-based test, that's
        // the SAME thread still inside CreateClient()/EnsureServer()
        // building THIS process's own TestServer -- and GetAccessTokenAsync's
        // "DevIdp" named HttpClient is self-referential by design (its own
        // FollowClientOptions.ClientId points back at this same process), so
        // without a real delay here, its handler factory can recurse into
        // that same WebApplicationFactory while it's still being built one
        // level up the call stack (found only by running this under a test
        // harness). A short, one-time startup delay is a negligible cost for
        // a background worker and avoids the whole class of hazard.
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        await Task.WhenAll(options.Value.AppIds.SelectMany(appId => EventTypes.Select(eventType => TailForeverAsync(appId, eventType, ct))));
    }

    private async Task TailForeverAsync(string appId, string eventType, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Always Replay from 0 -- see this class's own header comment.
                // A 404 (the event type not yet registered for this AppId --
                // no grant has happened yet) surfaces as an ordinary
                // exception via FollowClient's EnsureSuccessStatusCode and is
                // handled identically to a dropped connection: retried after
                // ReconnectDelay until the first grant's own
                // EnsureRegisteredAsync makes it exist.
                await foreach (var envelope in followClient.TailAsync(eventType, appId, fromSequenceNumber: 0, ct))
                    await ApplyAsync(appId, eventType, envelope, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RBAC fold for {AppId}/{EventType} lost its connection; reconnecting", appId, eventType);
            }

            await Task.Delay(ReconnectDelay, ct);
        }
    }

    private async Task ApplyAsync(string appId, string eventType, FollowedEventEnvelope envelope, CancellationToken ct)
    {
        var payload = envelope.Payload!;
        using var scope = scopeFactory.CreateScope();

        if (eventType == RoleGranted)
            await scope.ServiceProvider.GetRequiredService<RoleService>().AssignRoleAsync(
                payload["ActorId"]!.GetValue<string>(), appId, payload["RoleName"]!.GetValue<string>(), ct);
        else if (eventType == RoleRevoked)
            await scope.ServiceProvider.GetRequiredService<RoleService>().RevokeRoleAsync(
                payload["ActorId"]!.GetValue<string>(), appId, payload["RoleName"]!.GetValue<string>(), ct);
        else if (eventType == PermissionGranted)
            await scope.ServiceProvider.GetRequiredService<RoleService>().GrantDirectPermissionAsync(
                payload["ActorId"]!.GetValue<string>(), appId, payload["Permission"]!.GetValue<string>(), ct);
        else if (eventType == AppTrustRootRegistered)
            await scope.ServiceProvider.GetRequiredService<TrustRootService>().RegisterAsync(
                appId, payload["IssuerDid"]!.GetValue<string>(), payload["Description"]?.GetValue<string>(), ct);
    }
}
