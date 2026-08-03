using System.Security.Claims;
using EventStore.DevIdp;
using Microsoft.AspNetCore; // GetOpenIddictServerRequest() -- OpenIddictServerAspNetCoreHelpers
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(); // ADR-026 -- all three OTel signals, wired identically for every service

builder.Services.AddDbContext<DevIdpDbContext>(options =>
{
    options.UseInMemoryDatabase("EventStore.DevIdp");
    options.UseOpenIddict();
});

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

app.MapPost("/connect/token", async (HttpContext httpContext, IOpenIddictApplicationManager applicationManager) =>
{
    var request = httpContext.GetOpenIddictServerRequest() ??
        throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

    if (!request.IsClientCredentialsGrantType())
        throw new NotImplementedException("Only the client_credentials grant type is implemented.");

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
    // No id_token exists for client_credentials -- every claim set above goes
    // to the access token only.
    identity.SetDestinations(_ => [Destinations.AccessToken]);

    return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
});

app.Run();

public partial class Program;
