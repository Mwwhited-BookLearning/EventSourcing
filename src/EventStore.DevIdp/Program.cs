using System.Security.Claims;
using System.Security.Cryptography;
using EventStore.DevIdp;
using EventStore.Dpop;
using EventStore.Projections.Host;
using EventStore.TicketExchange;
using EventStore.Ucan;
using Microsoft.AspNetCore; // GetOpenIddictServerRequest() -- OpenIddictServerAspNetCoreHelpers
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Extensions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(); // ADR-026 -- all three OTel signals, wired identically for every service

// ADR-014's own deny-by-default CORS posture (EventStore.Host.Core.
// HostCoreExtensions.CorsPolicyName's exact policy shape: config-driven
// Cors:AllowedOrigins, AllowAnyMethod, only Authorization/Content-Type
// headers, no AllowCredentials -- bearer-only auth, never cookies) --
// this project never had its own copy of it (no browser client ever
// called this IdP's /connect/token endpoint directly until client-web's
// Vitals/Meridian instances did, this session): confirmed missing by
// actually opening this in a real browser, not assumed. Duplicated here
// rather than referencing EventStore.Host.Core directly -- that project
// pulls in the whole provider-agnostic Host composition root (auth
// policies, DPoP, request localization) DevIdp has no other reason to
// depend on, for one policy this small.
var devIdpAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(devIdpAllowedOrigins)
    .AllowAnyMethod()
    .WithHeaders("Authorization", "Content-Type", "DPoP")));

// A plain app.UseCors() call placed after Build() (this project's first
// attempt) never actually ran early enough: OpenIddict's own server
// middleware -- registered via ITS OWN IStartupFilter, activated by
// AddOpenIddict()...AddServer() below -- intercepts every request to its
// configured token endpoint URI unconditionally, REGARDLESS of where
// app.Use(...) calls appear in this file's own top-level statements.
// Confirmed directly: an OPTIONS preflight to /connect/token returned
// OpenIddict's own 400 ("only client_credentials/token-exchange grant
// types implemented") with no CORS headers at all, while the identical
// preflight against an ordinary MapPost/MapPut endpoint (/oauth/roles)
// got CORS's own correct 204 + headers -- proving the CORS policy/origin
// config itself was already right, and isolating the gap to specifically
// OpenIddict's request interception outrunning app.UseCors() for its own
// endpoint. IStartupFilter composition reverses registration order when
// folding (the framework's own documented behavior), so the FIRST
// IStartupFilter registered in the DI container ends up wrapping
// OUTERMOST -- i.e. runs first against every request. Registering this
// filter here, BEFORE AddOpenIddict() below ever registers its own, is
// what actually gets a plain UseCors() call to run before OpenIddict's
// interception -- reproduced and confirmed via a fast standalone run,
// not assumed from documentation alone.
builder.Services.AddTransient<IStartupFilter, CorsBeforeOpenIddictStartupFilter>();

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
// ADR-093 -- constructed here, not via a bare AddSingleton<T>() registration,
// so the SAME instance can be captured directly by the OpenIddict pipeline
// handler below (.AddServer(...) configures the pipeline before app.Build()
// exists, so there's no IServiceProvider yet to resolve a DI-registered
// singleton from at that point) as well as injected into the ordinary
// minimal-API endpoints via the registration on the next line.
var clientSecretRotationStore = new ClientSecretRotationStore();
builder.Services.AddSingleton(clientSecretRotationStore);
builder.Services.AddScoped<TrustRootService>(); // ADR-044
builder.Services.AddScoped<RoleService>(); // ADR-046
builder.Services.AddScoped<FederationService>(); // ADR-047
builder.Services.AddHttpClient(); // FederationService's own JWKS fetch

// ADR-067 -- RbacProjectionWorker's own Follow consumer. Registered
// unconditionally (HttpClient BaseAddress resolution is deferred until a
// request actually needs one), but the worker itself is a no-op whenever
// Rbac:AppIds is empty/unconfigured (the default) -- every pre-existing
// DevIdp-only test (no Host counterpart, no Rbac config section at all)
// is completely unaffected.
// Placeholder fallback BaseAddress, never a "!"-asserted required config
// value -- found only by running this: a real WebApplicationFactory-based
// test overrides BOTH the primary handler (TestServer-routed) AND this
// BaseAddress via a SECOND AddHttpClient("Follow"/"DevIdp", ...) call in its
// own ConfigureServices, but HttpClientFactoryOptions runs every registered
// HttpClientActions delegate for a given name in order -- if THIS one threw
// on a null config value, the test's own override never got a chance to run.
builder.Services.AddHttpClient("Follow", c => c.BaseAddress = new Uri(builder.Configuration["Rbac:HostBaseUrl"] ?? "http://unconfigured-rbac-host/"));
builder.Services.AddHttpClient("DevIdp", c => c.BaseAddress = new Uri(builder.Configuration["Rbac:DevIdpBaseAddress"] ?? "http://unconfigured-rbac-devidp/"));
builder.Services.Configure<FollowClientOptions>(builder.Configuration.GetSection("Rbac:Client"));
builder.Services.Configure<RbacProjectionOptions>(builder.Configuration.GetSection("Rbac"));
builder.Services.AddSingleton<FollowClient>();
builder.Services.AddHostedService<RbacProjectionWorker>();

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore().UseDbContext<DevIdpDbContext>();
        // ADR-093 -- OpenIddictApplicationCache's default in-memory entity
        // cache returns the SAME cached TApplication reference across every
        // scope/DbContext for a given ClientId; the rotate-secret endpoint's
        // own UpdateAsync call then deterministically throws
        // ConcurrencyException every attempt (confirmed only by actually
        // running this -- a bounded retry loop failed identically three
        // times in a row, ruling out a genuine transient conflict). No
        // application is ever created/updated at any real request rate
        // this dev/POC IdP serves, so disabling the cache costs nothing
        // here and removes a real, reproducible bug in the one place this
        // repo's own code ever calls UpdateAsync.
        options.DisableEntityCaching();
    })
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
        // Same fixed-allow-list problem exists for subject_token_type
        // (ID2032 again). But every one of RFC 8693's OWN registered types --
        // including the generic "jwt" one -- turned out (found only by
        // running this) to still trigger OpenIddict's built-in signature
        // check against ITS OWN configured signing keys before this code
        // ever runs, failing with ID2090 for every subject token here (a
        // self-signed UCAN delegation, a genuinely externally-issued
        // federated JWT -- never a token this IdP itself issued). A wholly
        // custom, unrecognized URN gets the same treatment "ticket" already
        // gets for requested_token_type above: OpenIddict has no built-in
        // format handler for a name it's never heard of, so it defers
        // entirely to this code's own sniff-the-JOSE-header branching below.
        options.Configure(o => o.SubjectTokenTypes.Add("urn:eventstore:token-type:external-subject"));
        // ADR-093 -- OpenIddict's own built-in ValidateClientSecret handler
        // runs unconditionally for EVERY grant type reaching this endpoint,
        // including Token Exchange -- found only by actually running this:
        // this file's own /connect/token delegate used to call
        // ValidateClientSecretAsync explicitly inside its exchange branch,
        // on the assumption that branch fully owned its own credential
        // check (no built-in handler for a custom grant type, the same
        // reasoning that held for requested_token_type/subject_token_type
        // above); a real rotated-secret request instead came back rejected
        // with OpenIddict's own ID2055 before that delegate ever ran. Real
        // zero-downtime rotation therefore has to intervene INSIDE
        // OpenIddict's own pipeline, not in application code: this handler
        // runs first (SetOrder(int.MinValue), the same technique the
        // ValidateTokenContext handler below already uses) and, if the
        // presented secret matches an unexpired PREVIOUS one for this
        // clientId, rewrites the request's own ClientSecret to the CURRENT
        // value before OpenIddict's built-in check ever sees it --
        // transparently, so that check still succeeds entirely on its own
        // terms.
        options.AddEventHandler<OpenIddictServerEvents.ValidateTokenRequestContext>(handler => handler
            .UseInlineHandler(context =>
            {
                var clientId = context.ClientId;
                var presentedSecret = context.Request?.ClientSecret;
                if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(presentedSecret)
                    && clientSecretRotationStore.MatchesUnexpiredPrevious(clientId, presentedSecret))
                {
                    var currentSecret = clientSecretRotationStore.CurrentOverrideOrNull(clientId) ?? DevIdpSeeder.GetClientSecret(clientId);
                    if (currentSecret is not null)
                        context.Request!.ClientSecret = currentSecret;
                }
                return default;
            })
            .SetOrder(int.MinValue));
        // Registering the type above only satisfies ValidateTokenExchangeParameters'
        // own presence/allow-list check. Decompiling OpenIddictServerHandlers
        // (this project's own "verify before citing" discipline, applied to
        // a third-party library this time, not just this repo's own docs)
        // found a SECOND, unconditional check: ValidateSubjectToken
        // (EvaluateValidatedTokens hardcodes RejectSubjectToken = true for
        // EVERY token-exchange request at this endpoint, with no options
        // flag to disable it) re-validates subject_token's OWN signature
        // against THIS server's own signing keys -- during Results.SignIn
        // itself, AFTER ExchangeUcanDelegationAsync/ExchangeFederatedTokenAsync
        // below have already done this token's REAL validation in full and
        // decided to approve it. Every subject_token this endpoint ever
        // receives is deliberately NOT signed by this IdP (a self-signed
        // UCAN delegation, ADR-043; a genuinely external federated token,
        // ADR-047), so that redundant built-in check always fails with
        // ID2090 ("signing key ... not found") and blocks an otherwise-
        // already-approved exchange. This inline handler -- ordered to run
        // before OpenIddict's own ValidateIdentityModelToken via
        // int.MinValue, which already skips its own logic once
        // context.Principal is non-null -- exists solely to stand in for
        // that redundant check for our one custom subject_token_type,
        // never for any of RFC 8693's own standard ones.
        options.AddEventHandler<OpenIddictServerEvents.ValidateTokenContext>(handler => handler
            .UseInlineHandler(context =>
            {
                // A non-null Principal alone isn't enough -- the very next
                // built-in handler (Protection.ValidatePrincipal) then
                // unconditionally requires the deserialized principal to
                // carry OpenIddict's OWN internal "oi_tkn_typ" claim (found
                // only by running this: "InvalidOperationException: The
                // deserialized principal doesn't contain the mandatory
                // 'oi_tkn_typ' claim"), checked against this exact
                // ValidTokenTypes set already populated above -- and, one
                // layer further still (also found only by running this:
                // "ID2184: the specified token doesn't contain any
                // presenter"), a presenter claim matching the caller's own
                // client_id, since our custom subject_token_type wasn't one
                // of the few standard ones ValidateSubjectToken special-
                // cases to skip presenter validation for.
                if (context.ValidTokenTypes.Contains("urn:eventstore:token-type:external-subject"))
                    context.Principal = new ClaimsPrincipal(new ClaimsIdentity())
                        .SetTokenType("urn:eventstore:token-type:external-subject")
                        .SetPresenters(context.Request?.ClientId ?? string.Empty);
                return default;
            })
            .SetOrder(int.MinValue));
        // ADR-030 -- "registry:admin:tenant-a" is one concrete AppId-scoped
        // admin variant, seeded for a dev/POC client to actually demonstrate
        // the mechanism; a real deployment would provision these per-tenant
        // dynamically, not via a fixed registered list like this one.
        // "registry:trust-admin" (ADR-044) is deliberately NOT implied by
        // "registry:admin" -- a caller needs both explicitly, never one
        // via the other.
        options.RegisterScopes("events:publish", "events:follow", "events:lineage:read", "registry:admin", "registry:admin:tenant-a", "registry:trust-admin", "telemetry:ingest", "telemetry:read", "attachments:ingest", "attachments:read", "peer:sync");

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
    TicketStore ticketStore, IOptionsMonitor<OpenIddictServerOptions> serverOptions,
    TrustRootService trustRootService, RoleService roleService, FederationService federationService) =>
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

        // ADR-093 -- a rotation-in-progress caller presenting its previous
        // secret never actually reaches this check as a failure case: the
        // ValidateTokenRequestContext handler registered above (.AddServer)
        // already rewrote request.ClientSecret to the current value before
        // OpenIddict's own built-in credential check ran; a request that
        // gets this far always carries an already-current-or-otherwise-
        // valid secret. This check stays as an explicit assertion at this
        // trust boundary rather than being removed outright.
        var callerApplication = await applicationManager.FindByClientIdAsync(request.ClientId);
        if (callerApplication is null || !await applicationManager.ValidateClientSecretAsync(callerApplication, request.ClientSecret))
            return Results.Json(new { error = "invalid_client" }, statusCode: StatusCodes.Status400BadRequest);

        if (request.RequestedTokenType == "urn:eventstore:token-type:ticket")
            return await IssueTicketAsync(request.SubjectToken, request.ClientId, ticketStore, serverOptions);

        // ADR-043/047 -- requesting an ordinary access token back (RFC
        // 8693's own standard token type) covers two of this item's three
        // Token Exchange use cases: a UCAN delegation being exchanged for
        // a bearer JWT carrying the delegated claims (ADR-043), or an
        // externally-issued, already-authoritative token being augmented
        // with this framework's own locally-known claims (ADR-047). Which
        // one applies is sniffed from the subject_token's own JOSE header --
        // a UcanDelegation always carries "typ": "ucan+jwt" (self-signed,
        // never issued by this IdP); anything else is treated as a
        // federated exchange candidate.
        if (request.RequestedTokenType is "urn:ietf:params:oauth:token-type:access_token" or null)
            return await ExchangeForAugmentedAccessTokenAsync(request, trustRootService, federationService, roleService, serverOptions, dpopResult.Jkt!);

        return Results.Json(new { error = "invalid_request", error_description = $"unsupported requested_token_type: {request.RequestedTokenType}" }, statusCode: StatusCodes.Status400BadRequest);
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
    // AddClaim, not SetClaim -- SetClaim REPLACES any existing claim of the
    // same type, so a client seeded with more than one claim sharing a type
    // (vitals-pi-client's own "review":"ae"/"review":"ionm" pair,
    // DevIdpSeeder.ExtraClaims) silently lost every value but the last one
    // looped over. Found via a real issued token's own decoded JWT payload
    // showing only "review":"ionm", never "ae" -- the exact class of bug
    // the sibling AddClaim(new Claim(...)) call sites below (rbac
    // permissions, delegated-grant capabilities, federated claims) already
    // avoid; this was the one remaining SetClaim call still looping over a
    // set that can legitimately hold more than one same-typed claim.
    foreach (var (claimType, claimValue) in DevIdpSeeder.GetExtraClaims(request.ClientId!))
        identity.AddClaim(new Claim(claimType, claimValue));
    // ADR-046 -- RBAC's own role-to-permission flattening, opt-in via a
    // new, non-standard "app_id" form parameter (Role/UserPermission are
    // AppId-scoped): every EXISTING client_credentials caller that never
    // passes app_id is completely unaffected, since this whole block is
    // skipped when it's absent.
    // ADR-066 -- dev-only step-up simulation, opt-in via a new, non-standard
    // "acr" form parameter, the same "opt-in, every existing caller
    // unaffected" shape "app_id" above already established. A real IdP
    // only ever grants a given acr value after ACTUALLY performing the
    // corresponding authentication method (password re-entry, WebAuthn,
    // ...); this dev/POC IdP has no interactive login at all for a
    // client_credentials (machine) caller to step up through, so it just
    // takes the caller's word for it -- the same "accept what's asserted,
    // no real verification" posture this IdP already takes for several
    // other dev-only simplifications (its own file header). auth_time is
    // set to the moment this token is issued, matching a real step-up's
    // own "just re-authenticated" semantics for RFC 9470's max_age check.
    var acr = (string?)request.GetParameter("acr");
    if (!string.IsNullOrEmpty(acr))
    {
        identity.SetClaim(Claims.AuthenticationContextReference, acr);
        // OpenIddict's own ValidateSignInDemand handler requires auth_time
        // to carry a genuinely numeric ClaimValueType -- found only by
        // running this against the real pipeline ("the auth_time claim...
        // is malformed or isn't of the expected type"), not by reading the
        // claim-setting code back. The plain SetClaim(string, string)
        // overload used for "acr" above sets a string-typed claim; the
        // dedicated SetClaim(string, long?) overload is what actually
        // produces the numeric type this specific claim needs.
        identity.SetClaim(Claims.AuthenticationTime, (long?)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    var rbacAppId = (string?)request.GetParameter("app_id");
    if (!string.IsNullOrEmpty(rbacAppId))
    {
        foreach (var permission in await roleService.GetFlattenedPermissionsAsync(request.ClientId!, rbacAppId))
        {
            var separatorIndex = permission.IndexOf(':');
            if (separatorIndex > 0)
                identity.AddClaim(new Claim(permission[..separatorIndex], permission[(separatorIndex + 1)..]));
        }
    }
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

// ADR-043/047's shared entry point -- requesting an ordinary access token
// back from Token Exchange. `app_id` is a new, non-standard form parameter
// this exchange needs (both AppTrustRoot and TrustedFederationIssuer
// registrations are AppId-scoped) -- there is no standard RFC 8693
// parameter for "which application namespace is this exchange scoped to."
async Task<IResult> ExchangeForAugmentedAccessTokenAsync(
    OpenIddictRequest request, TrustRootService trustRootService, FederationService federationService, RoleService roleService,
    IOptionsMonitor<OpenIddictServerOptions> serverOptions, string callerJkt)
{
    var subjectToken = request.SubjectToken;
    if (string.IsNullOrEmpty(subjectToken))
        return Results.Json(new { error = "invalid_request", error_description = "subject_token is required." }, statusCode: StatusCodes.Status400BadRequest);

    var appId = (string?)request.GetParameter("app_id");
    if (string.IsNullOrEmpty(appId))
        return Results.Json(new { error = "invalid_request", error_description = "app_id is required." }, statusCode: StatusCodes.Status400BadRequest);

    var subjectTyp = new JsonWebToken(subjectToken).Typ;
    return subjectTyp == "ucan+jwt"
        ? await ExchangeUcanDelegationAsync(subjectToken, appId, trustRootService, serverOptions, callerJkt)
        : await ExchangeFederatedTokenAsync(subjectToken, appId, federationService, roleService, callerJkt);
}

// ADR-043 step 2 (grantee side) -- verifies the delegation (self-signature,
// cap invariant against its own embedded proof, or AppTrustRoot if rooted
// directly) and, on success, issues an ordinary access token carrying
// exactly the delegated capabilities -- downstream code sees an ordinary
// bearer JWT, unaware it arrived via delegation (ADR-036's own
// consequence, reused). `callerJkt` -- the DPoP proof the grantee already
// presented on THIS exchange request itself, ADR-017 unaffected -- binds
// the issued token to the grantee's own key, the identical "cnf.jkt from
// the token request's own proof" pattern the client_credentials branch
// above already uses; omitting it would leave the issued token
// unusable against any DPoP-protected resource at all.
async Task<IResult> ExchangeUcanDelegationAsync(string delegationJwt, string appId, TrustRootService trustRootService, IOptionsMonitor<OpenIddictServerOptions> serverOptions, string callerJkt)
{
    var result = await UcanValidator.ValidateAsync(
        delegationJwt,
        () => Task.FromResult<IReadOnlyList<SecurityKey>>(serverOptions.CurrentValue.SigningCredentials.Select(c => c.Key).ToList()),
        (targetAppId, thumbprint) => trustRootService.IsTrustedAsync(targetAppId, thumbprint));

    if (!result.IsValid)
        return Results.Json(new { error = "invalid_grant", error_description = result.Error }, statusCode: StatusCodes.Status400BadRequest);
    if (result.AppId != appId)
        return Results.Json(new { error = "invalid_grant", error_description = "delegation's own appId does not match the requested app_id." }, statusCode: StatusCodes.Status400BadRequest);

    var identity = new ClaimsIdentity(
        authenticationType: TokenValidationParameters.DefaultAuthenticationType,
        nameType: Claims.Name,
        roleType: Claims.Role);
    identity.SetClaim(Claims.Subject, result.GranteeActorId);
    // The delegated claim(s) alone would still fail any endpoint gated by
    // an ordinary OAuth "scope" (a separate namespace, ADR-006) -- ADR-043
    // doesn't itself address scope-vs-claim interplay, so this grants the
    // one narrow scope this item's own exit criteria actually exercise a
    // delegated grant against (events:follow, revealField's own gate),
    // not a blanket scope grant.
    identity.SetScopes(["events:follow"]);
    foreach (var cap in result.Capabilities!)
    {
        var separatorIndex = cap.Claim.IndexOf(':');
        if (separatorIndex <= 0)
            continue;
        identity.AddClaim(new Claim(cap.Claim[..separatorIndex], cap.Claim[(separatorIndex + 1)..]));
        // ADR-043 -- the entity-scope restriction rides as a companion
        // claim (RequiredClaimEvaluator.HasClaimForEntity's own
        // convention), never encoded into the claim's own value.
        if (cap.EntityScope is not null)
            identity.AddClaim(new Claim($"{cap.Claim}:entityScope", cap.EntityScope));
    }
    // ADR-045 -- AccessLogReaderContext's own two markers: a token minted
    // via a delegated-grant exchange is "Attested" (ADR-045's own Attested
    // definition names ADR-043 delegations specifically), never
    // "Authoritative" -- and GrantRef records exactly WHICH delegation.
    identity.SetClaim("trust_basis", "Attested");
    if (result.GrantRef is { } grantRef)
        identity.SetClaim("grant_ref", grantRef.ToString());
    identity.SetClaim("cnf.jkt", callerJkt);
    identity.SetDestinations(_ => [Destinations.AccessToken]);

    return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}

// ADR-047 step (federated exchange) -- verifies the external token against
// its registered issuer's own JWKS, then augments -- never replaces -- its
// claims with this framework's own locally-known Role/UserPermission
// grants for the JIT-provisioned local ActorId. Identity claims (email,
// name, etc.) pass through completely unchanged; "sub" specifically is
// relocated to this system's own local ActorId (preserved verbatim under
// "federated_sub") since "sub" is this design's own load-bearing ActorId
// convention everywhere else, not merely inert identity metadata -- an
// honest, named deviation from "augments, never replaces" for that one
// claim, not silently glossed over.
async Task<IResult> ExchangeFederatedTokenAsync(string externalToken, string appId, FederationService federationService, RoleService roleService, string callerJkt)
{
    var externalJwt = new JsonWebToken(externalToken);
    var issuer = externalJwt.Issuer;
    if (string.IsNullOrEmpty(issuer))
        return Results.Json(new { error = "invalid_grant", error_description = "subject_token has no issuer." }, statusCode: StatusCodes.Status400BadRequest);

    var trustedIssuer = await federationService.FindAsync(appId, issuer);
    if (trustedIssuer is null)
        return Results.Json(new { error = "invalid_grant", error_description = "issuer is not a registered TrustedFederationIssuer for this app_id." }, statusCode: StatusCodes.Status400BadRequest);

    var signingKeys = await federationService.FetchSigningKeysAsync(trustedIssuer.JwksUri);
    var validationResult = await new JsonWebTokenHandler().ValidateTokenAsync(externalToken, new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = false,
        ValidateLifetime = true,
        IssuerSigningKeys = signingKeys,
    });
    if (!validationResult.IsValid)
        return Results.Json(new { error = "invalid_grant", error_description = "subject_token signature invalid against the registered issuer's own JWKS." }, statusCode: StatusCodes.Status400BadRequest);

    var externalClaims = validationResult.ClaimsIdentity!;
    var sub = externalClaims.FindFirst(Claims.Subject)?.Value;
    if (string.IsNullOrEmpty(sub))
        return Results.Json(new { error = "invalid_grant", error_description = "subject_token has no \"sub\" claim." }, statusCode: StatusCodes.Status400BadRequest);

    var actorId = await federationService.GetOrCreateActorIdAsync(appId, issuer, sub);

    var identity = new ClaimsIdentity(
        authenticationType: TokenValidationParameters.DefaultAuthenticationType,
        nameType: Claims.Name,
        roleType: Claims.Role);
    foreach (var claim in externalClaims.Claims.Where(c => c.Type != Claims.Subject))
        identity.AddClaim(new Claim(claim.Type, claim.Value));
    identity.SetClaim("federated_sub", sub);
    identity.SetClaim(Claims.Subject, actorId);
    identity.SetScopes(["events:follow"]); // same narrow-scope reasoning as ExchangeUcanDelegationAsync above

    foreach (var permission in await roleService.GetFlattenedPermissionsAsync(actorId, appId))
    {
        var separatorIndex = permission.IndexOf(':');
        if (separatorIndex > 0)
            identity.AddClaim(new Claim(permission[..separatorIndex], permission[(separatorIndex + 1)..]));
    }
    identity.SetClaim("cnf.jkt", callerJkt);
    identity.SetDestinations(_ => [Destinations.AccessToken]);

    return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
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
app.MapPost("/oauth/introspect", async (HttpContext httpContext, TicketStore ticketStore, ClientSecretRotationStore secretRotationStore) =>
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
    // ONLY place holding at all. ADR-093 -- secretRotationStore's own
    // per-app-instance override takes priority over DevIdpSeeder's static
    // seed value once a rotation has actually happened against THIS app.
    var secret = secretRotationStore.CurrentOverrideOrNull(ticket!.SecretRef) ?? DevIdpSeeder.GetClientSecret(ticket.SecretRef) ?? ticket.SecretRef;

    var expectedSig = HmacSigner.Sign(ticket.Value, secret!);
    // ADR-093 -- a ticket signed just before a rotation completed may carry
    // a signature computed against the PREVIOUS secret; DevIdpSeeder above
    // only ever knows the current one, so a mismatch here gets one more
    // chance against the tracked previous secret before actually failing.
    // A no-op for the one_time_secret path (SecretRef there is never a
    // registered clientId, so this never matches anything).
    if (!string.Equals(expectedSig, sig, StringComparison.Ordinal)
        && !string.Equals(HmacSigner.Sign(ticket.Value, secretRotationStore.PreviousOrEmpty(ticket.SecretRef)), sig, StringComparison.Ordinal))
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

// ADR-044/046/047's own admin surface -- plain, unauthenticated minimal-API
// endpoints (DevIdp has never had a runtime admin API/scope-gate of its
// own for anything it manages, including its seeded clients themselves,
// which are registered in code, not via an API at all) -- an honest,
// named narrowing, not an oversight: "registry:trust-admin"/
// "registry:admin" gate the CORE ENGINE's own registry endpoints
// (SchemaRegistryEndpoints, ADR-044's Consequences), never these
// DevIdp-internal management calls.
//
// ADR-067 retires 4 of this surface's original 6 endpoints: registering a
// trust root, assigning/revoking a role, and granting a direct permission
// are now real, hash-chained, scope-gated mutations published through
// EventStore.Rbac's own Host-side endpoints (RbacEndpoints.cs) -- this
// process only ever OBSERVES those via RbacProjectionWorker's Follow
// subscription now, folding into the SAME RoleService/TrustRootService
// tables below, unchanged. "PUT /oauth/roles" (defining what permissions a
// role NAME bundles) and "PUT /oauth/federation-issuers" are deliberately
// NOT retired -- ADR-067's own Decision names exactly 5 reserved event
// types (SchemaRegistered + the 4 above); a role's own permission-bundle
// definition and a federation issuer registration are neither one, and
// stay genuine DevIdp-internal configuration, same as ever.
app.MapPut("/oauth/roles", async (DefineRoleRequest request, RoleService roleService) =>
{
    await roleService.DefineRoleAsync(request.AppId, request.RoleName, request.Permissions);
    return Results.Created();
});

app.MapPut("/oauth/federation-issuers", async (RegisterFederationIssuerRequest request, FederationService federationService) =>
{
    await federationService.RegisterIssuerAsync(request.AppId, request.Issuer, request.JwksUri, request.Description);
    return Results.Created();
});

// ADR-093 -- real zero-downtime rotation for the ticket-exchange shared
// secret (ADR-040's client_id path): updates OpenIddict's own registered
// application record to the new secret, then records the OLD one in
// ClientSecretRotationStore as still-valid for OverlapWindow. Both the
// /connect/token token-exchange branch above and /oauth/introspect's HMAC
// check above already fall back to that store, so a caller mid-rotation
// (still presenting the old secret) keeps working until the window
// expires. Same unauthenticated-admin-surface posture as /oauth/roles and
// /oauth/federation-issuers above -- this dev/POC IdP has no runtime
// admin scope-gate for any of its own management calls.
app.MapPost("/oauth/clients/{clientId}/rotate-secret", async (
    string clientId, RotateClientSecretRequest request, IOpenIddictApplicationManager applicationManager, ClientSecretRotationStore secretRotationStore) =>
{
    if (await applicationManager.FindByClientIdAsync(clientId) is null)
        return Results.NotFound();

    // The CURRENT secret this rotation is moving away from -- either a
    // prior rotation's own override (this app instance already rotated
    // this clientId at least once) or DevIdpSeeder's original seed-time
    // value, whichever this app instance actually last validated against.
    var currentSecret = secretRotationStore.CurrentOverrideOrNull(clientId) ?? DevIdpSeeder.GetClientSecret(clientId);
    if (currentSecret is null)
        return Results.Problem($"'{clientId}' has no tracked current secret to rotate.", statusCode: StatusCodes.Status409Conflict);

    // A bounded re-fetch-and-retry against DbUpdateConcurrencyException --
    // textbook-correct handling for a genuine concurrent rotation request,
    // now that DisableEntityCaching() above (AddCore's own registration)
    // has removed the deterministic, every-single-time version of this
    // exception a stale cached application reference used to cause.
    for (var attempt = 1; ; attempt++)
    {
        var application = await applicationManager.FindByClientIdAsync(clientId)
            ?? throw new InvalidOperationException($"'{clientId}' disappeared mid-rotation.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await applicationManager.PopulateAsync(descriptor, application);
        descriptor.ClientSecret = request.NewClientSecret;
        try
        {
            await applicationManager.UpdateAsync(application, descriptor);
            break;
        }
        catch (OpenIddictExceptions.ConcurrencyException) when (attempt < 3)
        {
        }
    }

    // ADR-093's own Decision: "the rotation cadence/schedule itself stays
    // ops-configurable" -- the overlap window is a caller-supplied value,
    // not a framework constant, with a reasonable default for a caller
    // that doesn't need a specific one.
    secretRotationStore.RecordPrevious(clientId, currentSecret, request.OverlapWindow ?? TimeSpan.FromHours(24));
    secretRotationStore.SetCurrent(clientId, request.NewClientSecret);

    return Results.Ok();
});

app.Run();

record DefineRoleRequest(string AppId, string RoleName, List<string> Permissions);
record RegisterFederationIssuerRequest(string AppId, string Issuer, string JwksUri, string? Description);
record RotateClientSecretRequest(string NewClientSecret, TimeSpan? OverlapWindow);

// Registered before AddOpenIddict() specifically so this ends up the
// OUTERMOST IStartupFilter (see the registration site's own comment) --
// its UseCors() call then runs against every request before OpenIddict's
// own middleware ever gets a chance to intercept one.
public sealed class CorsBeforeOpenIddictStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseCors();
        next(app);
    };
}

public partial class Program;
