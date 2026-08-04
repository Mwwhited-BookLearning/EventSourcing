using System.Security.Claims;
using EventStore.DevIdp;
using EventStore.Dpop;
using Microsoft.AspNetCore; // GetOpenIddictServerRequest() -- OpenIddictServerAspNetCoreHelpers
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(); // ADR-026 -- all three OTel signals, wired identically for every service

// A fresh, unique name per process start (computed once, outside the options
// delegate below -- AddDbContext re-invokes that delegate on every scoped
// DbContext instantiation, so a Guid generated INSIDE it would hand every
// single request its own fresh, empty, unseeded database). EF Core's
// InMemory provider keys databases by name process-wide, not per
// WebApplicationFactory instance, so a fixed name would let two DevIdp
// TestServers running concurrently in the same test process (e.g.
// AuthSqliteTests and ProjectionsSqliteTests) silently share one store and
// race on seeding.
var devIdpDatabaseName = $"EventStore.DevIdp-{Guid.NewGuid():N}";
builder.Services.AddDbContext<DevIdpDbContext>(options =>
{
    options.UseInMemoryDatabase(devIdpDatabaseName);
    options.UseOpenIddict();
});
builder.Services.AddSingleton<IDpopReplayCache, InMemoryDpopReplayCache>();

builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<DevIdpDbContext>())
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("/connect/token");
        options.AllowClientCredentialsFlow();
        options.RegisterScopes("events:publish", "events:follow", "events:lineage:read", "registry:admin");

        // Dev-only ephemeral certs (ADR-006) -- a real deployment swaps DevIdp
        // for a production IdP entirely, per this item's own config-only story.
        options.AddDevelopmentEncryptionCertificate();
        options.AddDevelopmentSigningCertificate();

        // A validating Host's ordinary JwtBearer middleware reads a plain signed
        // JWT via the discovery document + JWKS -- it has no OpenIddict-specific
        // decryption code, so the access token must NOT be an encrypted JWE.
        options.DisableAccessTokenEncryption();

        options.UseAspNetCore()
            .EnableTokenEndpointPassthrough()
            .DisableTransportSecurityRequirement(); // dev-only plain-HTTP token endpoint, ADR-006
    });

var app = builder.Build();
app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DevIdpDbContext>().Database.EnsureCreatedAsync();
    await DevIdpSeeder.SeedAsync(scope.ServiceProvider);
}

app.MapPost("/connect/token", async (HttpContext httpContext, IOpenIddictApplicationManager applicationManager, IDpopReplayCache replayCache) =>
{
    var request = httpContext.GetOpenIddictServerRequest() ??
        throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

    if (!request.IsClientCredentialsGrantType())
        throw new NotImplementedException("Only the client_credentials grant type is implemented.");

    // ADR-017 -- every token request itself must present a DPoP proof (RFC
    // 9449 §5); no ath expected here since no access token exists yet. A
    // failing proof gets OAuth2's own error shape (400, invalid_dpop_proof),
    // not RFC 9457 Problem Details -- that shape is this project's contract
    // for EventStore.Host.<Provider>'s API surface (03-api-contracts.md),
    // not for this OAuth2 token endpoint.
    var htu = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}{httpContext.Request.Path}";
    var proofHeader = httpContext.Request.Headers["DPoP"].ToString();
    var dpopResult = await DpopProofValidator.ValidateAsync(proofHeader, httpContext.Request.Method, htu, expectedAth: null, replayCache);
    if (!dpopResult.IsValid)
        return Results.Json(new { error = "invalid_dpop_proof", error_description = dpopResult.Error }, statusCode: StatusCodes.Status400BadRequest);

    // OpenIddict's own token-request middleware already validated client_id/
    // client_secret before this delegate runs -- an invalid secret never
    // reaches here (see options.UseAspNetCore().EnableTokenEndpointPassthrough()).
    var application = await applicationManager.FindByClientIdAsync(request.ClientId!) ??
        throw new InvalidOperationException("The application details cannot be found in the database.");

    var identity = new ClaimsIdentity(
        authenticationType: TokenValidationParameters.DefaultAuthenticationType,
        nameType: Claims.Name,
        roleType: Claims.Role);

    identity.SetClaim(Claims.Subject, await applicationManager.GetClientIdAsync(application));
    identity.SetScopes(request.GetScopes());
    // ADR-017 -- binds the issued access token to the key that just proved
    // possession above; a flat "cnf.jkt" claim (the ADR's own phrasing),
    // not RFC 9449's nested cnf:{jkt:...} JSON-object shape.
    identity.SetClaim("cnf.jkt", dpopResult.Jkt);
    // No id_token exists for client_credentials -- every claim set above goes
    // to the access token only.
    identity.SetDestinations(_ => [Destinations.AccessToken]);

    return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
});

app.Run();

public partial class Program;
