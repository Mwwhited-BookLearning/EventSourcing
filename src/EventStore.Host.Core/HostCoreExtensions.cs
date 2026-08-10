using System.Net;
using System.Net.Security;
using EventStore.Dpop;
using EventStore.Spiffe;
using EventStore.TicketExchange;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Host.Core;

// Shared, provider-agnostic composition root (docs/06-solution-structure.md).
// Per ADR-001, exactly one thing varies per deployable -- the DbContext's
// provider registration (UseSqlite/UseNpgsql/UseSqlServer) plus the matching
// IJsonPathTranslator/migrations-assembly choice -- and that lives in each
// EventStore.Host.<Provider> project itself, not here. Everything else that
// doesn't vary by provider (auth, CORS, spec generation, endpoint mapping)
// is added here incrementally by the build-plan items that introduce it.
public static class HostCoreExtensions
{
    public const string CorsPolicyName = "EventStoreCors";

    public static WebApplicationBuilder AddEventStoreCommonServices(this WebApplicationBuilder builder)
    {
        // ADR-006: OAuth2 Client Credentials against an OIDC provider's own
        // discovery document -- no OpenIddict-specific code here at all, so
        // swapping EventStore.DevIdp for a production IdP later is config-only.
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = builder.Configuration["Authentication:Authority"];
                options.RequireHttpsMetadata = builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", true);
                options.TokenValidationParameters.ValidateAudience = false;
            })
            // ADR-040 -- additive, never the default scheme: only the specific
            // routes that opt in (Streaming playback, Attachment retrieval)
            // ever try this scheme at all, via AuthorizeAttribute.
            // AuthenticationSchemes; every other endpoint's Bearer-only
            // authentication is completely unaffected.
            .AddScheme<TicketAuthenticationOptions, TicketAuthenticationHandler>(TicketAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.IntrospectionEndpoint = new Uri(new Uri(builder.Configuration["Authentication:Authority"]!), "oauth/introspect").ToString();
            });

        // Needed by TicketAuthenticationHandler's own introspection call --
        // registered here (not left to happen to already exist) so this
        // scheme is self-contained regardless of what else a given Host
        // process happens to also register a named HttpClient for.
        builder.Services.AddHttpClient();

        // ADR-017 -- one replay cache per Host process; dev/POC scale, per
        // IDpopReplayCache's own accepted-cost note.
        builder.Services.AddSingleton<IDpopReplayCache, InMemoryDpopReplayCache>();

        builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("events:publish", p => p.Requirements.Add(new ScopeRequirement("events:publish")))
            .AddPolicy("events:follow", p => p.Requirements.Add(new ScopeRequirement("events:follow")))
            .AddPolicy("events:lineage:read", p => p.Requirements.Add(new ScopeRequirement("events:lineage:read")))
            .AddPolicy("registry:admin", p => p.Requirements.Add(new ScopeRequirement("registry:admin")))
            // ADR-044 -- deliberately its own policy, not implied by
            // registry:admin (Program.cs's own comment on this scope
            // separation, unchanged by "Control-Plane Actions as Reserved
            // Events" moving AppTrustRoot's write path here from DevIdp).
            .AddPolicy("registry:trust-admin", p => p.Requirements.Add(new ScopeRequirement("registry:trust-admin")))
            .AddPolicy("telemetry:ingest", p => p.Requirements.Add(new ScopeRequirement("telemetry:ingest")))
            .AddPolicy("telemetry:read", p => p.Requirements.Add(new ScopeRequirement("telemetry:read")))
            .AddPolicy("attachments:ingest", p => p.Requirements.Add(new ScopeRequirement("attachments:ingest")))
            .AddPolicy("attachments:read", p => p.Requirements.Add(new ScopeRequirement("attachments:read")))
            .AddPolicy("peer:sync", p => p.Requirements.Add(new ScopeRequirement("peer:sync")));

        // ADR-014: deny by default -- an empty/missing Cors:AllowedOrigins means
        // no cross-origin browser call ever succeeds (server-to-server traffic,
        // which never sends Origin, is unaffected). AllowCredentials() is
        // deliberately never set -- bearer-only auth, never cookies.
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        builder.Services.AddCors(options => options.AddPolicy(CorsPolicyName, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod() // includes QUERY (ADR-012), which always preflights
            .WithHeaders("Authorization", "Content-Type")));

        return builder;
    }

    // ADR-048 -- moves ADR-033's peer-sync authentication onto SPIFFE/SPIRE's
    // cross-trust-domain federation, additively: PeerSyncClient/PeerSyncEndpoints'
    // existing OAuth2/DPoP bearer auth (ADR-006/017, "peer:sync" scope) is
    // completely unaffected; this adds a SECOND, transport-level mTLS gate on
    // top of it, on the internal listener specifically. Returns the identity
    // so a Host's own Program.cs can log/inspect it if needed -- most callers
    // just need the side effect (HttpClient + optional Kestrel listener wired).
    public static SpiffePeerIdentity AddSpiffePeerIdentity(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("Spiffe").Get<SpiffePeerOptions>() ?? new SpiffePeerOptions();
        var identity = new SpiffePeerIdentity(options);

        builder.Services.AddSingleton(identity);

        // Attaches this Host's own SVID as a client certificate on every
        // outbound peer-sync call (PeerSyncClient) -- built before Build(),
        // so the exact same identity object also configures the internal
        // listener below, rather than resolving a second instance from DI.
        builder.Services.AddHttpClient("PeerSync")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions { ClientCertificates = [identity.SvidCertificate] },
            });

        // null (the default) means no internal mTLS listener starts at all --
        // e.g. under test, or a single-site deployment with no peers. A real
        // multi-site deployment sets Spiffe:InternalListenPort explicitly.
        if (options.InternalListenPort is { } port)
        {
            var allowedPaths = options.AllowedInternalCallerPaths.Count > 0
                ? options.AllowedInternalCallerPaths
                : [options.ServicePath];
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenInternalMtls(
                new IPEndPoint(IPAddress.Any, port), identity.SvidCertificate, identity.TrustBundle,
                id => allowedPaths.Contains(id.Path)));
        }

        return identity;
    }

    public static WebApplication MapEventStoreCommonEndpoints(this WebApplication app)
    {
        app.UseCors(CorsPolicyName);
        app.UseAuthentication();
        app.UseDpopValidation(); // ADR-017 -- after authentication (needs the validated bearer's cnf.jkt), before authorization (short-circuits a DPoP failure before any scope policy could otherwise let it through)
        app.UseAuthorization();
        return app;
    }
}
