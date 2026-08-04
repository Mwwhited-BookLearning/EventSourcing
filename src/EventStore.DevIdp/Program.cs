using System.Security.Claims;
using System.Security.Cryptography;
using EventStore.DevIdp;
using EventStore.Dpop;
using EventStore.TicketExchange;
using Microsoft.AspNetCore; // GetOpenIddictServerRequest() -- OpenIddictServerAspNetCoreHelpers
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
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
builder.Services.AddSingleton<TicketStore>(); // ADR-040 -- in-process, non-persistent, per auth.md's own "client/token state lives in DevIdp" statement

builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<DevIdpDbContext>())
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("/connect/token");
        options.AllowClientCredentialsFlow();
        // ADR-040 -- OpenIddict actually has a dedicated, first-class method
        // for RFC 8693 (found only by reflecting the real installed
        // assembly: AllowCustomFlow throws for this specific grant type,
        // "already assigned to a standard grant type," since OpenIddict
        // recognizes token-exchange by name even with no built-in handler
        // of its own for it). Its own built-in validation handler checks
        // requested_token_type against a fixed allow-list too (rejecting
        // this ADR's own "urn:eventstore:token-type:ticket" with
        // "ID2032: requested_token_type is not supported" until added here).
        options.AllowTokenExchangeFlow();
        options.Configure(o => o.RequestedTokenTypes.Add("urn:eventstore:token-type:ticket"));
        // ADR-030 -- "registry:admin:tenant-a" is one concrete AppId-scoped
        // admin variant, seeded for a dev/POC client to actually demonstrate
        // the mechanism; a real deployment would provision these per-tenant
        // dynamically, not via a fixed registered list like this one.
        options.RegisterScopes("events:publish", "events:follow", "events:lineage:read", "registry:admin", "registry:admin:tenant-a", "telemetry:ingest", "telemetry:read", "attachments:ingest", "attachments:read", "peer:sync");

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

app.MapPost("/connect/token", async (
    HttpContext httpContext, IOpenIddictApplicationManager applicationManager, IDpopReplayCache replayCache,
    TicketStore ticketStore, IOptionsMonitor<OpenIddictServerOptions> serverOptions) =>
{
    var request = httpContext.GetOpenIddictServerRequest() ??
        throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

    if (!request.IsClientCredentialsGrantType() && !request.IsTokenExchangeGrantType())
        throw new NotImplementedException("Only the client_credentials and token-exchange grant types are implemented.");

    // ADR-017 -- every token request itself must present a DPoP proof (RFC
    // 9449 §5); no ath expected here since no access token exists yet. A
    // failing proof gets OAuth2's own error shape (400, invalid_dpop_proof),
    // not RFC 9457 Problem Details -- that shape is this project's contract
    // for EventStore.Host.<Provider>'s API surface (03-api-contracts.md),
    // not for this OAuth2 token endpoint. Applies to BOTH grant types --
    // ADR-040's own sequence diagram shows the ticket-issuance request as
    // "a normal, header-based, DPoP-bound request... ADR-017 still applies
    // there in full."
    var htu = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}{httpContext.Request.Path}";
    var proofHeader = httpContext.Request.Headers["DPoP"].ToString();
    var dpopResult = await DpopProofValidator.ValidateAsync(proofHeader, httpContext.Request.Method, htu, expectedAth: null, replayCache);
    if (!dpopResult.IsValid)
        return Results.Json(new { error = "invalid_dpop_proof", error_description = dpopResult.Error }, statusCode: StatusCodes.Status400BadRequest);

    if (request.IsTokenExchangeGrantType())
    {
        // OpenIddict's own /connect/token pipeline unconditionally requires
        // client_id for ANY grant type reaching this delegate (confirmed
        // only by actually running this: "ID2029: the mandatory client_id
        // parameter is missing") -- genuinely incompatible with ADR-040's
        // own one_time_secret path ("never requires a registered
        // client_id"). That path is handled by the separate, non-OpenIddict
        // /oauth/ticket-exchange endpoint below instead; this branch only
        // ever serves the client_id (+ client_secret) path.
        if (string.IsNullOrEmpty(request.ClientId) || string.IsNullOrEmpty(request.ClientSecret))
            return Results.Json(new { error = "invalid_client", error_description = "client_id and client_secret are required on this endpoint -- use /oauth/ticket-exchange for the one_time_secret path." }, statusCode: StatusCodes.Status400BadRequest);

        var callerApplication = await applicationManager.FindByClientIdAsync(request.ClientId);
        if (callerApplication is null || !await applicationManager.ValidateClientSecretAsync(callerApplication, request.ClientSecret))
            return Results.Json(new { error = "invalid_client" }, statusCode: StatusCodes.Status400BadRequest);

        return await IssueTicketAsync(request.SubjectToken, request.ClientId, ticketStore, serverOptions);
    }

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
    foreach (var (claimType, claimValue) in DevIdpSeeder.GetExtraClaims(request.ClientId!))
        identity.SetClaim(claimType, claimValue);
    // ADR-017 -- binds the issued access token to the key that just proved
    // possession above; a flat "cnf.jkt" claim (the ADR's own phrasing),
    // not RFC 9449's nested cnf:{jkt:...} JSON-object shape.
    identity.SetClaim("cnf.jkt", dpopResult.Jkt);
    // No id_token exists for client_credentials -- every claim set above goes
    // to the access token only.
    identity.SetDestinations(_ => [Destinations.AccessToken]);

    return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
});

// ADR-040 step 1's shared core -- RFC 8693 Token Exchange, requesting a
// ticket (requested_token_type = "urn:eventstore:token-type:ticket")
// rather than a fresh access token. Not a Results.SignIn -- a ticket is
// deliberately NOT an OpenIddict-issued token at all (opaque, not a JWT,
// not self-describing), so this returns a plain JSON body directly,
// matching the ADR's own sequence diagram's literal `{ ticket, expiresIn }`
// response shape rather than RFC 8693's own `access_token`/`expires_in`
// field names. Shared by both the client_id path (/connect/token, above)
// and the one_time_secret path (/oauth/ticket-exchange, below) -- the only
// difference between the two is how `secretRef` was already validated
// before this is called.
async Task<IResult> IssueTicketAsync(string? subjectToken, string secretRef, TicketStore ticketStore, IOptionsMonitor<OpenIddictServerOptions> serverOptions)
{
    if (string.IsNullOrEmpty(subjectToken))
        return Results.Json(new { error = "invalid_request", error_description = "subject_token is required." }, statusCode: StatusCodes.Status400BadRequest);

    var signingKeys = serverOptions.CurrentValue.SigningCredentials.Select(c => c.Key).ToList();
    var tokenHandler = new JsonWebTokenHandler();
    var validationResult = await tokenHandler.ValidateTokenAsync(subjectToken, new TokenValidationParameters
    {
        // No fixed Issuer is configured for this dev/POC IdP (ADR-006's own
        // "config-only swap to a real IdP later" story never named one) --
        // validating signature + lifetime against this SAME process's own
        // signing keys is what actually matters here; issuer/audience
        // checks would just compare a value against itself.
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        IssuerSigningKeys = signingKeys,
    });
    if (!validationResult.IsValid)
        return Results.Json(new { error = "invalid_grant", error_description = "subject_token is invalid, malformed, or expired." }, statusCode: StatusCodes.Status400BadRequest);

    var subjectClaims = validationResult.ClaimsIdentity!.Claims
        .Where(c => TicketClaims.ExcludedClaimTypes.Contains(c.Type) is false)
        .Select(c => (c.Type, c.Value))
        .ToList();

    var ticketValue = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    // Deliberately shorter than a normal access token's lifetime (ADR-040) --
    // 60 seconds is enough for "compute sig, set src, browser issues the GET"
    // without ever needing to be a durable, renewable credential.
    var expiresIn = TimeSpan.FromSeconds(60);
    ticketStore.Add(new Ticket(ticketValue, secretRef, DateTimeOffset.UtcNow.Add(expiresIn), subjectClaims));

    return Results.Json(new { ticket = ticketValue, expiresIn = (int)expiresIn.TotalSeconds, issuedTokenType = "urn:eventstore:token-type:ticket" });
}

// ADR-040's one_time_secret path -- deliberately NOT routed through
// OpenIddict's own /connect/token pipeline at all (GetOpenIddictServerRequest
// is never called here), since that pipeline unconditionally requires a
// registered client_id for any grant type reaching it, which this path is
// specifically designed never to need. A plain minimal-API endpoint reading
// form fields directly, doing its own DPoP check (ADR-017 still applies in
// full, same as the client_id path), then sharing IssueTicketAsync's core.
app.MapPost("/oauth/ticket-exchange", async (HttpContext httpContext, IDpopReplayCache replayCache, TicketStore ticketStore, IOptionsMonitor<OpenIddictServerOptions> serverOptions) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var subjectToken = form["subject_token"].ToString();
    var oneTimeSecret = form["one_time_secret"].ToString();

    if (string.IsNullOrEmpty(oneTimeSecret))
        return Results.Json(new { error = "invalid_request", error_description = "one_time_secret is required on this endpoint -- use /connect/token for the client_id path." }, statusCode: StatusCodes.Status400BadRequest);

    var htu = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}{httpContext.Request.Path}";
    var proofHeader = httpContext.Request.Headers["DPoP"].ToString();
    var dpopResult = await DpopProofValidator.ValidateAsync(proofHeader, httpContext.Request.Method, htu, expectedAth: null, replayCache);
    if (!dpopResult.IsValid)
        return Results.Json(new { error = "invalid_dpop_proof", error_description = dpopResult.Error }, statusCode: StatusCodes.Status400BadRequest);

    // Never persisted anywhere else (ADR-040) -- this Ticket record IS the
    // only place secretRef is held, for exactly as long as the ticket itself lives.
    return await IssueTicketAsync(subjectToken, oneTimeSecret, ticketStore, serverOptions);
});

// ADR-040 step 3 -- an RFC 7662-shaped introspection call, extended with
// `sig`. Deliberately a plain, unauthenticated minimal-API endpoint (the
// ADR names no credential the calling Streaming/Attachment service itself
// presents here) -- the receiving service never holds a shared secret and
// never verifies a signature itself, it only ever forwards ticket+sig here.
app.MapPost("/oauth/introspect", async (HttpContext httpContext, TicketStore ticketStore) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var tokenValue = form["token"].ToString();
    var sig = form["sig"].ToString();

    if (string.IsNullOrEmpty(tokenValue) || !ticketStore.TryGet(tokenValue, out var ticket))
        return Results.Json(new { active = false });

    // The shared secret is re-derived, never re-transmitted: the client_id
    // path re-looks-up the SAME dev-only plaintext client_secret
    // DevIdpSeeder already holds (IOpenIddictApplicationManager itself
    // never exposes a stored secret in plaintext, only validates a
    // provided one -- correct, but incompatible with recomputing an HMAC
    // server-side, so this reads the same source DevIdpSeeder's own
    // "dev-only plaintext secrets" comment already names); the
    // one_time_secret path reads back the value the Ticket record is the
    // ONLY place holding at all.
    var secret = DevIdpSeeder.GetClientSecret(ticket!.SecretRef) ?? ticket.SecretRef;

    var expectedSig = HmacSigner.Sign(ticket.Value, secret!);
    if (!string.Equals(expectedSig, sig, StringComparison.Ordinal))
        return Results.Json(new { active = false });

    // Single-use, and only burned on a SUCCESSFUL (signature-matching)
    // resolution -- a wrong-signature presentation above never reaches
    // here, so it can never burn the ticket for a later, correctly-signed
    // retry (ADR-040's own stated distinction between the two threats).
    if (!ticket.TryMarkConsumed())
        return Results.Json(new { active = false });

    return Results.Json(new
    {
        active = true,
        claims = ticket.OriginalTokenClaims.Select(c => new { type = c.Type, value = c.Value }),
    });
});

app.Run();

public partial class Program;
