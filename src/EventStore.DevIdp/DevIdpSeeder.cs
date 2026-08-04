using System.Collections.Concurrent;
using EventStore.Dpop;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace EventStore.DevIdp;

// The Client Credentials clients ADR-006/features/auth.md name explicitly
// (plus one added per later build-plan item -- projections-client for
// "CQRS Read-Model Projections", tenant-a-operator-client for "Multi-
// Tenancy"), seeded in code at startup -- no realm-export file, no admin
// console, matching this item's own dev/POC scope. Secrets below are
// plaintext, fixed, dev-only values, not production credentials.
public static class DevIdpSeeder
{
    private static readonly (string ClientId, string ClientSecret, string[] Scopes)[] Clients =
    [
        ("publisher-client", "publisher-client-secret", ["events:publish"]),
        ("follower-client", "follower-client-secret", ["events:follow", "events:lineage:read"]),
        ("operator-client", "operator-client-secret", ["registry:admin"]),
        ("projections-client", "projections-client-secret", ["events:follow"]), // "CQRS Read-Model Projections" -- ProjectionHost is a Follow caller like any other (ADR-015)
        ("tenant-a-operator-client", "tenant-a-operator-client-secret", ["registry:admin:tenant-a"]), // "Multi-Tenancy" (ADR-030) -- a caller scoped to exactly one AppId, not the unscoped framework-operator form
        ("telemetry-client", "telemetry-client-secret", ["telemetry:ingest", "telemetry:read"]), // "Streaming Channels" (ADR-031) -- a producer/detector client, holding both since this repo's tests drive both roles from one caller
        ("attachments-client", "attachments-client-secret", ["attachments:ingest", "attachments:read"]), // "Binary Attachments" (ADR-032) -- same both-roles-in-one-caller posture as telemetry-client
        ("peer-sync-client", "peer-sync-client-secret", ["peer:sync", "events:publish", "registry:admin"]), // "Sharding & Replication" (ADR-033) -- shared by every site in this dev/POC environment; a real deployment would give each site its own credential, per-site OriginId identity comes from the application layer (/peer-sync/whoami), not this token. Also holds events:publish/registry:admin since this repo's HTTP replication test drives register+publish+sync from one caller, the same both-roles-in-one-caller posture as telemetry-client/attachments-client
    ];

    // ADR-017 -- "each of the four OAuth2 clients generates its own
    // asymmetric key pair." No separate client process exists in this repo
    // (every client is simulated by a test/demo harness), so this seeder
    // plays that role too: one key pair per client, generated once at
    // startup and held here for whoever is acting as that client to sign
    // DPoP proofs with. The token endpoint itself never reads this map --
    // it validates whatever proof is actually submitted, self-contained,
    // exactly as RFC 9449 requires.
    private static readonly ConcurrentDictionary<string, DpopKeyPair> ClientKeys = new();

    public static DpopKeyPair GetClientKeyPair(string clientId) => ClientKeys[clientId];

    public static async Task SeedAsync(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var (clientId, clientSecret, scopes) in Clients)
        {
            ClientKeys.TryAdd(clientId, DpopKeyPair.Generate());

            if (await manager.FindByClientIdAsync(clientId) is not null)
                continue;

            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                },
            };
            foreach (var scope in scopes)
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);

            await manager.CreateAsync(descriptor);
        }
    }
}
