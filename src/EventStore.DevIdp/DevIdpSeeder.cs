using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace EventStore.DevIdp;

// The three Client Credentials clients ADR-006/features/auth.md name
// explicitly, seeded in code at startup -- no realm-export file, no admin
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
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var (clientId, clientSecret, scopes) in Clients)
        {
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
