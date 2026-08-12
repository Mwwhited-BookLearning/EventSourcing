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
        ("operator-client", "operator-client-secret", ["registry:admin", "registry:trust-admin"]),
        ("projections-client", "projections-client-secret", ["events:follow"]), // "CQRS Read-Model Projections" -- ProjectionHost is a Follow caller like any other (ADR-015)
        ("tenant-a-operator-client", "tenant-a-operator-client-secret", ["registry:admin:tenant-a"]), // "Multi-Tenancy" (ADR-030) -- a caller scoped to exactly one AppId, not the unscoped framework-operator form
        ("telemetry-client", "telemetry-client-secret", ["telemetry:ingest", "telemetry:read"]), // "Streaming Channels" (ADR-031) -- a producer/detector client, holding both since this repo's tests drive both roles from one caller
        ("attachments-client", "attachments-client-secret", ["attachments:ingest", "attachments:read"]), // "Binary Attachments" (ADR-032) -- same both-roles-in-one-caller posture as telemetry-client
        ("peer-sync-client", "peer-sync-client-secret", ["peer:sync", "events:publish", "registry:admin"]), // "Sharding & Replication" (ADR-033) -- shared by every site in this dev/POC environment; a real deployment would give each site its own credential, per-site OriginId identity comes from the application layer (/peer-sync/whoami), not this token. Also holds events:publish/registry:admin since this repo's HTTP replication test drives register+publish+sync from one caller, the same both-roles-in-one-caller posture as telemetry-client/attachments-client
        ("clinician-spa-client", "clinician-spa-client-secret", ["telemetry:read", "attachments:read"]), // "Ticket Exchange for Header-Incapable Clients" (ADR-040) -- the header-CAPABLE caller (an SPA/backend) that exchanges its own bearer token for a ticket on behalf of a <video src>/<img src> element it doesn't control the request internals of; named after the ADR/feature-doc's own running example, not a generic reuse of telemetry-client/attachments-client
        ("colleague-client", "colleague-client-secret", []), // "Delegated Grants, RBAC, Federated Claims" (ADR-043) -- the grantee of a "secondary opinion" delegation; holds no scopes/claims of its own at all, everything it can do comes from what clinician-spa-client (the granter) delegates
        ("devidp-rbac-follower-client", "devidp-rbac-follower-client-secret", ["events:follow"]), // "Control-Plane Actions as Reserved Events" (ADR-067) -- RbacProjectionWorker's own identity when it tails the Host's RoleGranted/RoleRevoked/PermissionGranted/AppTrustRootRegistered events; distinct from follower-client/projections-client so this fold's own access is independently auditable/revocable
        ("composer-client", "composer-client-secret", ["events:publish", "registry:admin"]), // "Proving-Ground Application UX" -- client-web's generic Event Composer tab needs registry:admin to list registered event types/schemas (RegistryQueries.eventTypes/eventType) AND events:publish to actually submit one, the same both-roles-in-one-caller posture telemetry-client/attachments-client/peer-sync-client already establish; deliberately its own identity rather than widening follower-client (a read-only browsing identity) with admin rights
        // "Domain Decision Queues" -- a Principal Investigator/analyst is a
        // distinct real-world actor from the generic Composer tool, so this
        // gets its OWN identity rather than widening composer-client's
        // claims (which would let every Composer user impersonate clinical/
        // compliance authority) -- the same "one identity per real
        // capability need" reasoning composer-client itself was added
        // under. events:publish only, no registry:admin -- these callers
        // only ever publish an already-known "authorityDecision", never
        // list/introspect the schema registry. review:ae/review:ionm/
        // consent:approve is the union of every decision claim
        // VitalsSharedTypes.EnsureAuthorityDecisionRegisteredAsync's three
        // callers each register -- "a PrincipalInvestigator's real role
        // bundle already carries every decision claim this domain names"
        // (ADR-043's own comment on this exact union).
        ("vitals-pi-client", "vitals-pi-client-secret", ["events:publish"]),
        ("meridian-analyst-client", "meridian-analyst-client-secret", ["events:publish"]),
    ];

    // ADR-040/043 -- only a caller that legitimately constructs header-
    // incapable playback/retrieval URLs, or exchanges a UCAN delegation/
    // federated token for an ordinary access token, is granted the token-
    // exchange grant type; every other seeded client keeps its existing
    // client_credentials-only permission set unchanged.
    private static readonly HashSet<string> TokenExchangeClients = ["clinician-spa-client", "colleague-client"];

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

    // ADR-008/050's RequiredClaims and ADR-009's x-masking.requiredClaim are
    // a deliberately SEPARATE "type:value" claim namespace from OAuth scopes
    // (RequiredClaimEvaluator.HasClaim -- ClaimsPrincipal.HasClaim(type,
    // value), never the "scope" claim) -- OpenIddict's own Permissions system
    // only governs which scopes a client may request, so a real issued token
    // needs these added as their own claims too. Added for "GraphQL-Only
    // Query Layer"'s revealField mutation, the first item to need this proven
    // through a genuine DevIdp-issued token rather than an in-process
    // ClaimsPrincipal construction -- purely additive to follower-client's
    // existing scopes, nothing prior depended on its absence.
    private static readonly IReadOnlyDictionary<string, (string Type, string Value)[]> ExtraClaims = new Dictionary<string, (string Type, string Value)[]>
    {
        ["follower-client"] = [("pii", "view")],
        // "Delegated Grants" (ADR-043) -- the "user holding special
        // authority" the ADR's own Context names (a doctor with
        // clearance:phi); clinician-spa-client plays this granter role,
        // same as it already plays the header-capable requesting party
        // for ADR-040's ticket exchange.
        ["clinician-spa-client"] = [("clearance", "phi")],
        // "Domain Decision Queues" -- see this file's own Clients-array
        // comment on vitals-pi-client/meridian-analyst-client for the full
        // reasoning on why these are separate identities.
        ["vitals-pi-client"] = [("review", "ae"), ("review", "ionm"), ("consent", "approve")],
        ["meridian-analyst-client"] = [("identity", "aml-review")],
    };

    public static IReadOnlyList<(string Type, string Value)> GetExtraClaims(string clientId) =>
        ExtraClaims.TryGetValue(clientId, out var claims) ? claims : [];

    // ADR-040's introspection step needs the PLAINTEXT client_secret to
    // recompute HMAC-SHA256(ticket, secret) -- OpenIddict's own
    // IOpenIddictApplicationManager deliberately never exposes a stored
    // secret in plaintext (only ValidateClientSecretAsync, correctly, for
    // security), so this reads back from the SAME dev-only plaintext
    // source this file's own header comment already names, rather than a
    // second, newly-invented secrets store.
    public static string? GetClientSecret(string clientId) =>
        Clients.FirstOrDefault(c => c.ClientId == clientId).ClientSecret;

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

            if (TokenExchangeClients.Contains(clientId))
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.TokenExchange);

            await manager.CreateAsync(descriptor);
        }
    }
}
