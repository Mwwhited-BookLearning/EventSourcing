using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
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
            });

        builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("events:publish", p => p.Requirements.Add(new ScopeRequirement("events:publish")))
            .AddPolicy("events:follow", p => p.Requirements.Add(new ScopeRequirement("events:follow")))
            .AddPolicy("events:lineage:read", p => p.Requirements.Add(new ScopeRequirement("events:lineage:read")))
            .AddPolicy("registry:admin", p => p.Requirements.Add(new ScopeRequirement("registry:admin")));

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

    public static WebApplication MapEventStoreCommonEndpoints(this WebApplication app)
    {
        app.UseCors(CorsPolicyName);
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
