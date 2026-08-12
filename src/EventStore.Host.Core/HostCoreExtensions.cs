using System.Globalization;
using System.Net;
using System.Net.Security;
using EventStore.Dpop;
using EventStore.Spiffe;
using EventStore.TicketExchange;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Localization;
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
            .AddPolicy("peer:sync", p => p.Requirements.Add(new ScopeRequirement("peer:sync")))
            // ADR-060 -- webhooks.md's own illustrative scope choice (03-api-
            // contracts.md never enumerated a webhook-management endpoint at
            // all); registering/rotating a subscription is an admin action
            // on par with registry:admin, kept as its own named policy rather
            // than folded into it since a caller managing webhook targets has
            // no inherent need for schema-registry admin rights or vice versa.
            .AddPolicy("webhooks:admin", p => p.Requirements.Add(new ScopeRequirement("webhooks:admin")));

        // ADR-014: deny by default -- an empty/missing Cors:AllowedOrigins means
        // no cross-origin browser call ever succeeds (server-to-server traffic,
        // which never sends Origin, is unaffected). AllowCredentials() is
        // deliberately never set -- bearer-only auth, never cookies.
        //
        // "DPoP" added to the allow-list this session -- ADR-017's resource-
        // server requirement (DpopValidationMiddleware, below) predates this
        // policy's own header list, which never accounted for it: any
        // browser client sending a real DPoP proof (client-web, once it
        // started sending one) had every request rejected at the CORS
        // preflight stage before DpopValidationMiddleware ever got a chance
        // to see it. Found only by actually driving client-web against a
        // live eventstore in a real browser.
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        builder.Services.AddCors(options => options.AddPolicy(CorsPolicyName, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod() // includes QUERY (ADR-012), which always preflights
            .WithHeaders("Authorization", "Content-Type", "DPoP")));

        // ADR-087 -- RFC 9110 §12 Accept-Language negotiation via ASP.NET
        // Core's own first-party RequestLocalizationMiddleware, never
        // hand-rolled header parsing. Verified directly against the real
        // installed assembly before adopting: a request naming an
        // unsupported culture (or none) falls back to DefaultRequestCulture
        // rather than erroring, and ApplyCurrentCultureToResponseHeaders
        // echoes the NEGOTIATED culture back as a real `Content-Language`
        // response header -- the value client-web's own locale-detection
        // reads to select which locale's translation resources a
        // ViewDefinition template resolves its keys against (this doc's
        // own "the client-side consequence of server-side negotiation,"
        // mvvm-client.md). `ar-SA` is included specifically so an RTL
        // locale is always negotiable, not merely a hypothetical.
        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            CultureInfo[] supportedCultures = [new("en-US"), new("fr-FR"), new("ar-SA")];
            options.DefaultRequestCulture = new RequestCulture("en-US");
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
            options.ApplyCurrentCultureToResponseHeaders = true;
        });

        // ADR-013 -- registers IProblemDetailsService; combined with
        // UseExceptionHandler() below, ASP.NET Core's own hosting layer
        // automatically writes an RFC 9457 Problem Details body for any
        // client/server error response that doesn't already carry one
        // (an unhandled exception, or a minimal-API result like
        // Results.Forbid()/NotFound()/Conflict() that sets a status code
        // with no body) -- no per-endpoint code needed for those. An
        // endpoint that needs to carry occurrence-specific detail (e.g.
        // `missingParentEventIds`) still calls Results.Problem(...)
        // explicitly, the one case this registration alone can't cover.
        builder.Services.AddProblemDetails();

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
        // ADR-013 -- outermost middleware, so it can catch an unhandled
        // exception from anything downstream (CORS/auth/DPoP/routing/the
        // endpoint itself) and, via AddProblemDetails() above, write a
        // real RFC 9457 body for it rather than an empty 500.
        app.UseExceptionHandler();
        // UseExceptionHandler alone only covers unhandled EXCEPTIONS --
        // a response that reaches an error status code with no body some
        // OTHER way (the authorization middleware's own ForbidAsync/
        // ChallengeAsync for a policy failure, a bare Results.Forbid()/
        // NotFound()/Conflict()) needs UseStatusCodePages() specifically;
        // AddProblemDetails() alone does not retrofit those (found only by
        // running this: a policy-rejected request came back with NO body
        // and a null Content-Type until this line was added, not the
        // Problem Details response the reasoning above assumed).
        app.UseStatusCodePages();

        // ADR-087 -- must run before anything that could read CultureInfo.
        // CurrentCulture or write the Content-Language response header;
        // first in the pipeline, ahead of CORS/auth, matches Microsoft's
        // own documented placement for this middleware.
        app.UseRequestLocalization();
        app.UseCors(CorsPolicyName);
        app.UseAuthentication();
        app.UseDpopValidation(); // ADR-017 -- after authentication (needs the validated bearer's cnf.jkt), before authorization (short-circuits a DPoP failure before any scope policy could otherwise let it through)
        app.UseAuthorization();
        return app;
    }
}
