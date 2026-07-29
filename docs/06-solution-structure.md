# Solution Structure

## Project layout

```
EventStore.sln
  src/
    EventStore.Domain/              -- entities, no EF dependency
    EventStore.Persistence/         -- DbContext, repositories, IJsonPathTranslator interface + all 3 impls
    EventStore.Persistence.Migrations.Sqlite/
    EventStore.Persistence.Migrations.Postgres/
    EventStore.Persistence.Migrations.SqlServer/
    EventStore.SchemaRegistry/      -- registration service, validation, ParentLinkService
    EventStore.Publish.Api/         -- POST /publish/{event-type}
    EventStore.Follow.Api/          -- QUERY /follow/{event-type} (SSE), OData parsing (ADR-012)
    EventStore.Lineage.Api/         -- QUERY /events/{id}/parents|children|ancestors|descendants (ADR-012)
    EventStore.SpecGeneration/      -- OpenAPI + AsyncAPI builders
    EventStore.Host.Core/           -- shared, provider-agnostic composition root logic (see below)
    EventStore.Host.Sqlite/         -- the actual deployable: Host.Core + SQLite wiring (ADR-001)
    EventStore.Host.Postgres/       -- the actual deployable: Host.Core + PostgreSQL wiring
    EventStore.Host.SqlServer/      -- the actual deployable: Host.Core + SQL Server wiring
    EventStore.DevIdp/              -- dev-only OpenIddict token issuer, in-process (see below)
    EventStore.ServiceDefaults/     -- Aspire scaffolding: OpenTelemetry, health checks, service discovery defaults
    EventStore.AppHost/             -- Aspire orchestration for local dev/POC (see below)

    -- CQRS read side (09-cqrs-read-models.md, ADR-015/016) -- a separate
    -- deployable, a separate database, talking to the write side only via
    -- QUERY /follow like any other consumer:
    EventStore.Projections.Abstractions/  -- IProjection<T>, ChangeKind-agnostic; projection authors depend on only this
    EventStore.Projections.Host/          -- ProjectionHost, SnapshotMerger, ProjectionsDbContext (ProjectionCheckpoint, ProjectionSnapshot)
    Samples.Orders.Projections/           -- worked example: OrderSummaryProjection (features/cqrs-projections.md)
  tests/
    EventStore.UnitTests/
    EventStore.IntegrationTests/    -- runs against all three providers (see below)
    EventStore.Bdd/                 -- Reqnroll/SpecFlow-style step definitions for *.feature files
```

There is no single `EventStore.Host` project. Per `ADR-001`, provider
selection is a build-time choice — `EventStore.Host.Sqlite`,
`.Postgres`, and `.SqlServer` are three separate, independently
publishable ASP.NET Core executables, each referencing
`EventStore.Host.Core` for everything that doesn't depend on which
provider is active (endpoint mapping, auth, spec generation, schema
registry, etc.) and adding only its own provider's ~5 lines of DI
registration. There is no `Database:Provider` configuration value
anywhere — "which provider" is answered by "which of the three projects
you built and deployed."

`EventStore.Bdd`'s `*.feature` files are real, tool-executed Gherkin — copy
them out of the fenced ```` ```gherkin ``` ```` blocks in
`../docs/features/*.md` when scaffolding this project. The design package
keeps the narrative doc (context + sequence diagram + Gherkin) as the single
source during design; once implementation starts, the extracted `.feature`
files become the executable source of truth for BDD tests and are free to
diverge (e.g. gain step-definition-specific tags) — resync manually if the
design doc changes.

## Why separate migrations projects

EF Core migrations embed provider-specific SQL in their generated `Up()`/
`Down()` methods — they are not portable even when the model is identical.
Each `*.Migrations.<Provider>` project references
`EventStore.Persistence` and holds only that provider's migration history.
`dotnet ef migrations add <Name> --project src/EventStore.Persistence.Migrations.Sqlite --startup-project src/EventStore.Host.Sqlite` and equivalent per provider (the startup project is now the matching `EventStore.Host.<Provider>`, not a single shared host).

## DI wiring (composition root)

Per `ADR-001`, exactly one of the four blocks below (DbContext,
`IJsonPathTranslator`, `IEventLineageQueryProvider`, migrations assembly)
varies per provider — and it's the **only** thing that varies. Everything
else lives once, in `EventStore.Host.Core`, and is shared by all three
deployables.

### `EventStore.Host.Core` — shared, provider-agnostic

```csharp
public static class HostCoreExtensions
{
    public static void AddEventStoreCommonServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ISchemaRegistryReader, SchemaRegistryReader>();
        builder.Services.AddScoped<SchemaValidationService>();
        builder.Services.AddScoped<ParentLinkService>();
        builder.Services.AddScoped<ODataFilterParser>();
        builder.Services.AddProblemDetails(); // ADR-013: one error shape, every endpoint
        builder.Services.AddMemoryCache(); // backs the ~60s spec-document cache, ADR-002
        builder.Services.AddSingleton<EventSchemaConverter>();      // JsonSchema text -> shared Microsoft.OpenApi OpenApiSchema
        builder.Services.AddSingleton<MaskingSchemaTransformer>();  // schema-level x-masking -> oneOf[value,masked] wrapper
        builder.Services.AddSingleton<OpenApiDocumentBuilder>();
        builder.Services.AddSingleton<AsyncApiDocumentBuilder>();

        builder.Services.AddCors(o => o.AddPolicy("EventStoreCors", policy =>
        {
            var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            policy.WithOrigins(origins) // ADR-014: empty by default -- deny every browser origin until configured
                  .WithMethods("GET", "PUT", "QUERY") // QUERY per ADR-012 -- a non-simple method, always preflighted
                  .WithHeaders("Authorization", "Content-Type");
            // no .AllowCredentials() -- Bearer-in-header only, never cookies
        }));

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = builder.Configuration["Authentication:Authority"]; // dev: EventStore.DevIdp's base URL
                options.Audience = builder.Configuration["Authentication:Audience"];
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment(); // DevIdp runs over plain HTTP locally
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("events:publish", p => p.Requirements.Add(new ScopeRequirement("events:publish")))
            .AddPolicy("events:follow", p => p.Requirements.Add(new ScopeRequirement("events:follow")))
            .AddPolicy("events:lineage:read", p => p.Requirements.Add(new ScopeRequirement("events:lineage:read")))
            .AddPolicy("registry:admin", p => p.Requirements.Add(new ScopeRequirement("registry:admin")));
    }

    public static void MapEventStoreCommonEndpoints(this WebApplication app)
    {
        app.UseCors("EventStoreCors"); // ADR-014 -- before endpoint mapping, applies to all of them
        // /publish, /follow, /events/{id}/..., /registry, /openapi.json, /asyncapi.json
    }
}
```

### `EventStore.Host.<Provider>` — the only per-provider code

```csharp
// EventStore.Host.Sqlite/Program.cs (Postgres/SqlServer variants are the same shape,
// swapping UseSqlite/the translator/the query provider/the migrations assembly)
var builder = WebApplication.CreateBuilder(args);
builder.AddEventStoreCommonServices();

builder.Services.AddDbContext<EventStoreContext>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("Sqlite"),
    x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite")));
builder.Services.AddScoped<IJsonPathTranslator, SqliteJsonPathTranslator>();
builder.Services.AddScoped<IEventLineageQueryProvider, SqliteEventLineageQueryProvider>();

var app = builder.Build();
app.MapEventStoreCommonEndpoints();
app.Run();
```

No `switch`, no `Database:Provider` config key, no risk of routing to the
wrong migrations assembly — the provider is fixed by which project you
built. `IJsonPathTranslator`/`IEventLineageQueryProvider`'s three
implementation classes still live centrally in `EventStore.Persistence`
(they're just classes); only the one-line DI *registration* choosing which
implementation moves per host project.

`ScopeRequirement`/its handler is a small custom `IAuthorizationHandler` — not
the built-in `RequireClaim` — because OAuth2 delivers `scope` as one
space-delimited string claim (`"events:publish events:follow"`), and
`RequireClaim` does an exact-value match, not a "one of the space-separated
tokens" match. See `ADR-006`.

### Spec generation — one shared schema model, two document builders

Add the `Microsoft.OpenApi` NuGet package (the official .NET OpenAPI 3.1
object model) to `EventStore.SpecGeneration`. It's used for more than just
building `openapi.json`: `EventSchemaConverter` parses every active event
type's registered `JsonSchema` text into `Microsoft.OpenApi.Models.OpenApiSchema`
**once**, and both builders share that same object — there is no separate
AsyncAPI-flavored schema representation. This works because AsyncAPI 3.0
deliberately reuses OpenAPI's Schema Object dialect, and because
`OpenApiSchema.Extensions` carries unrecognized keywords (including custom
ones like `x-masking`) through rather than dropping them (verify this with
a round-trip unit test on an unusual keyword — see `ADR-002`'s
consequences).

```csharp
public class EventSchemaConverter
{
    public OpenApiSchema Parse(string jsonSchemaText) => /* Microsoft.OpenApi reader */;
}

public class MaskingSchemaTransformer
{
    // Schema-level, claims-independent -- NOT the same thing as IPayloadMasker below.
    // Runs once per document build; the wire *shape* is identical for every caller.
    public OpenApiSchema Wrap(OpenApiSchema schema) { /* recurse; wrap x-masking nodes */ }
}
```

`OpenApiDocumentBuilder` uses the **unwrapped** `OpenApiSchema` and builds
the whole `OpenApiDocument` (paths, security schemes, info) natively via
`Microsoft.OpenApi`'s own object model, serialized with
`document.SerializeAsV31(writer)` — no hand-rolled JSON on the OpenAPI side
at all.

`AsyncApiDocumentBuilder` runs each schema through
`MaskingSchemaTransformer.Wrap(...)` first, then hand-builds the
channels/messages/operations/components envelope as a
`System.Text.Json.Nodes.JsonObject` tree, embedding the transformed
schema's `Microsoft.OpenApi`-serialized JSON into `components.schemas`.
There's no mature .NET library for this half — a unit test that parses
each generated `asyncapi.json` back against the published AsyncAPI 3.0
JSON Schema is the safety net a type system would otherwise provide.

Both endpoints are thin routes mapped by `MapEventStoreCommonEndpoints`
(`EventStore.Host.Core`, shared by all three provider deployables):

```csharp
app.MapGet("/openapi.json", async (OpenApiDocumentBuilder b) =>
    Results.Text(await b.GetOrBuildJsonAsync(), "application/json")).AllowAnonymous();
app.MapGet("/asyncapi.json", async (AsyncApiDocumentBuilder b) =>
    Results.Text(await b.GetOrBuildJsonAsync(), "application/json")).AllowAnonymous();

// ADR-025 -- pure presentation over the two endpoints above, both anonymous, no second generation path
app.MapScalarApiReference(); // Scalar.AspNetCore -- serves the OpenAPI docs UI at /scalar
app.MapGet("/asyncapi-ui", () => Results.Content(AsyncApiUiPage.Html, "text/html")).AllowAnonymous();
// AsyncApiUiPage.Html: a small static page loading @asyncapi/react-component via CDN, pointed at /asyncapi.json
```

Each builder's `GetOrBuildJsonAsync()` checks `IMemoryCache` first
(`"openapi-document"` / `"asyncapi-document"`, ~60s absolute expiration per
`ADR-002`); on a miss it calls `ISchemaRegistryReader.GetActiveEventTypesAsync()`
and rebuilds. `SchemaRegistryService` calls `IMemoryCache.Remove(...)` on
both keys after a successful registration (`05-schema-registry-and-spec-
generation.md`, registration step 10) — that's the entire invalidation
mechanism, no pub/sub or distributed cache needed for a single instance
(see `ADR-002`'s consequences for the multi-instance caveat).

**`MaskingSchemaTransformer` is not deferred alongside masking.** It's
schema-only and claims-independent, so it's needed the moment
`AsyncApiDocumentBuilder` exists (Phase 4) — only `IPayloadMasker` below
(the data-level, per-caller half) is deprioritized to Phase 8. Factor the
"find every `x-masking` node" tree-walk into one shared helper both
transformers call, so the recursion rule (scalar node / scalar array
`items` / property nested inside complex-object `items`) is implemented
once.

### Event-type security (required claims) — why this isn't a fifth `AddPolicy`

`RequiredPublishClaim`/`RequiredReadClaim` (`ADR-008`) can't be wired as
static ASP.NET Core policies the way the four scopes are: a policy's
requirement is fixed at startup, but which claim is required depends on
*which event type* the request names, which is only known once the route
value is bound and the registry is queried. So this check is plain
application code, run after the `EventTypeDefinition` is loaded — not a
declarative `[Authorize(Policy = "...")]`:

```csharp
static bool HasRequiredClaim(ClaimsPrincipal user, string? requiredClaim)
{
    if (requiredClaim is null) return true;
    var (type, value) = SplitOnce(requiredClaim, ':');
    return user.HasClaim(type, value); // a single discrete claim -- the built-in check is fine here,
}                                       // unlike ScopeRequirement's space-delimited-claim problem above
```

Called from `PublishEndpoint` (against `RequiredPublishClaim`, after
resolving the active `EventTypeDefinition`, before schema validation) and
`FollowEndpoint` (against `RequiredReadClaim` for its own event type, once
at connect time, alongside the `$filter`-field validation) exactly as a
single pass/fail check. `LineageEndpoint` uses it differently, per
`ADR-008`'s "you can only see what you can see": once for the root
`{eventId}`'s own type (pass/fail, `403` if it fails — you can't query the
lineage of something you can't see), then again, independently, for every
*other* distinct `EventType` the traversal discovers — a failure there
doesn't reject the request, it turns that one node into a `restricted:
true` stub (see `03-api-contracts.md`) without affecting any other node in
the response.

```csharp
// LineageEndpoint (illustrative): build the restricted-type set once,
// same primitive as the root check, then consult it per discovered node
var restrictedTypes = allEventTypesInResult
    .Where(t => t.RequiredReadClaim is not null && !HasRequiredClaim(user, t.RequiredReadClaim))
    .Select(t => t.Name)
    .ToHashSet();
// a node whose EventType is in restrictedTypes becomes { eventId, resolved: true, restricted: true }
```

### Publish idempotency (`ADR-011`) — the concurrent-retry edge case

The lookup-then-insert sketched in `05-schema-registry-and-spec-generation.md`
has an obvious race: two concurrent retries carrying the same *never-yet-seen*
`eventId` can both pass the "not found" lookup before either commits.
`EventAppender` must catch that at the database level, not assume the
lookup was exclusive:

```csharp
try
{
    await db.Events.AddAsync(newEvent);
    await db.SaveChangesAsync();
    return PublishResult.Created(newEvent);
}
catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, nameof(StoredEvent.EventId)))
{
    // Lost the race -- someone else's insert for this EventId committed first.
    // Not a real error: re-run the same "found, compare hash" path the
    // pre-check would have taken if it had run a moment later.
    var existing = await db.Events.SingleAsync(e => e.EventId == newEvent.EventId);
    return existing.PayloadHash == newEvent.PayloadHash
        ? PublishResult.IdempotentReplay(existing)
        : PublishResult.Conflict();
}
```

### Hash chain (`ADR-019`) — computed in the same `EventAppender` step

`ChainHash` is computed immediately before the insert above, inside the
same transaction, off whichever row currently has the highest
`SequenceNumber` (there is no separate "get the chain tail" query beyond
that one lookup, already needed to assign the next `SequenceNumber`):

```csharp
var priorChainHash = await db.Events
    .OrderByDescending(e => e.SequenceNumber)
    .Select(e => e.ChainHash)
    .FirstOrDefaultAsync() ?? SeedChainHash; // fixed constant for SequenceNumber = 1

newEvent.ChainHash = Sha256Hex($"{priorChainHash}|{newEvent.PayloadHash}|{newEvent.SequenceNumber}");
```

Identical on every provider — no `IJsonPathTranslator`-style abstraction
needed, since this is plain application code, not a query pushed to SQL.

### IPayloadMasker — the data half of masking, deprioritized to a later phase

This is the second of masking's two halves — `MaskingSchemaTransformer`
above is the schema half, and is *not* deprioritized. `IPayloadMasker` is
design-complete but scheduled after Phases 0–6, per the user's own
sequencing call — recorded here so the shape isn't lost, not because it's
blocked on anything technical.

The transform is a pure function of the extended `JsonSchema` (the one
carrying `x-masking`) and the current payload data — nothing endpoint- or
transport-specific:

```csharp
public interface IPayloadMasker
{
    // Pure: only needs the schema and the data. Claim-checking is injected,
    // not resolved internally -- this knows nothing about ClaimsPrincipal,
    // HttpContext, or where the data came from.
    JsonNode Mask(JsonSchema schema, JsonNode payload, Func<string, bool> hasClaim);
}
```

Internally it walks the schema recursively, exactly per `ADR-009`'s rule:
a scalar property carrying `x-masking` wraps its value; an array's `items`
carrying it (when `items` is scalar) wraps each element; a property nested
inside a complex-object `items` schema wraps just that property per
element. None of that recursion needs anything beyond `schema` and
`payload` — `hasClaim` is only consulted at the leaves where `x-masking`
actually appears. `x-masking.regulatoryClassification`/`governanceBody`/
`regulationReference` are read by nothing in this transform — they're
schema-only documentation (`02-data-model.md`), so `IPayloadMasker` simply
never looks at them.

Because it's a pure `(schema, data) -> data` step with claim-checking
injected, it composes as a link in a small command chain rather than logic
embedded in `FollowEndpoint` specifically:

```csharp
// FollowEndpoint's per-event pipeline (illustrative):
var maskedPayload = payloadMasker.Mask(activeSchema, rawPayload, claimType => user.HasClaim(...));
```

The *set* of claims to check is fixed for the life of one Follow connection
(same JWT throughout), so `hasClaim` can close over a claim set computed
once at connect time — but the masker itself doesn't know or care that
that's how its caller chose to supply it. A future direct "read event by
id" endpoint reuses `IPayloadMasker` unchanged; only the surrounding
pipeline (an ASP.NET Core middleware for a discrete request/response, or
an explicit per-event step for a long-lived SSE connection like today's
Follow) differs per transport. The stored `Payload` is never touched by
any of this — masking is computed fresh at the response boundary, for
whichever caller is asking.

Per-deployment build is the accepted v1 approach (three artifacts, one per
provider, no runtime config value) — see `ADR-001`. Each
`EventStore.Host.<Provider>` passes its own migrations assembly directly to
`UseSqlite`/`UseNpgsql`/`UseSqlServer` (shown in the DI wiring section
above) — there's no assembly-selection logic to get wrong at startup,
because each deployable only ever has one to choose from.

## Routing `QUERY` and reading its body (`ADR-012`)

`MapMethods` accepts any method string — ASP.NET Core routing has no fixed
verb enum, so `QUERY` needs no framework changes:

```csharp
app.MapMethods("/follow/{eventType}", ["QUERY"], FollowEndpoint.Handle);
app.MapMethods("/events/{eventId}/ancestors", ["QUERY"], LineageEndpoint.GetAncestors);
// ...and the other three Lineage routes, and QUERY /registry
```

The request body (`application/x-www-form-urlencoded`) is read via
`HttpRequest.ReadFormAsync()`/`Request.Form`, which is exactly the API
shape `Request.Query` already has (`IFormCollection` mirrors
`IQueryCollection`) — every place that used to read
`Request.Query["$filter"]` now reads `(await Request.ReadFormAsync())["$filter"]`,
same string, same `ODataFilterParser`, nothing else changes. `$top`/`$skip`
on the Lineage and Registry-list endpoints are read the same way.

## Follow: tail vs replay cursor (`ADR-010`)

`FollowEndpoint` is one continuous poll loop
(`04-odata-filter-pushdown.md`: `WHERE SequenceNumber > lastSeen AND
predicate`) regardless of `mode` — only how `lastSeen` is *initialized*,
once, at connect time, differs:

```csharp
long lastSeen = mode switch
{
    FollowMode.Tail   => await db.Events.MaxAsync(e => (long?)e.SequenceNumber) ?? 0,
    FollowMode.Replay => fromSequenceNumber ?? 0,
    _ => throw new UnreachableException()
};
// then the existing poll loop runs unchanged from here, for either mode
```

`fromSequenceNumber` is rejected with `400` before this point if
`mode != Replay` — validated alongside the `$filter`-field check, same
place `RequiredReadClaim` is checked (`ADR-008`). No new persistence, no
per-consumer state: the caller supplies the cursor on every connection: it
isn't remembered server-side between connections.

## Event lineage (parent/child DAG) queries

`parents`/`children` are plain LINQ joins against `EventParents` — fully
portable, no raw SQL needed. `ancestors`/`descendants` need a recursive
query; EF Core's LINQ provider has no translation for recursive CTEs, so
these are the one query path in the store that isn't a pure `IQueryable`:

```csharp
public interface IEventLineageQueryProvider
{
    Task<IReadOnlyList<LineageNode>> GetAncestorsAsync(Guid eventId, IReadOnlySet<string> restrictedTypes);
    Task<IReadOnlyList<LineageNode>> GetDescendantsAsync(Guid eventId, IReadOnlySet<string> restrictedTypes);
}
```

Each provider implementation issues a `WITH RECURSIVE` CTE (SQLite,
PostgreSQL) or `WITH` CTE (SQL Server — no `RECURSIVE` keyword required)
via `FromSqlInterpolated`/raw SQL, resolved via DI exactly like
`IJsonPathTranslator`.

**Cycle safety is mandatory, not conditional on `ParentValidationMode`** —
see `ADR-005` for why a cycle can exist even starting from a `Strict` event.
PostgreSQL and SQL Server recursive CTEs support a native `CYCLE` clause;
SQLite has none, so track a visited-path column/array in the CTE and stop
recursing when a node reappears. Cap traversal depth in all three as a
belt-and-suspenders limit regardless of provider.

**`restrictedTypes` must stop the recursion itself, not just redact the
final output** (`ADR-008`): the recursive term's `WHERE` clause excludes
expanding through any row whose `EventType` is in `restrictedTypes` —
that row is still returned once, as the `restricted: true` leaf, but the
CTE never joins onward from it. A provider that fully expanded the graph
and only masked which fields get *serialized* would still leak a
restricted node's position and connectivity through the shape of what's
returned beyond it — the exact leak `ADR-008` exists to prevent. This is
the same "leaf, don't recurse past it" treatment `resolved: false` already
gets, just for a different reason, and both need the same care taken in
the recursive term.

## Event upcasting (`ADR-018`)

Unlike `IJsonPathTranslator`, there is no per-`(EventType, FromVersion)`
class to write. `upcastFromPrevious` (registered per version,
`05-schema-registry-and-spec-generation.md`) is an OData `compute()`
expression list — **data**, not code — so `UpcastChain` is one generic
executor that evaluates it via the same `Microsoft.OData.UriParser`
already used for `$filter` (`04-odata-filter-pushdown.md`):

```csharp
public class UpcastChain
{
    private readonly ISchemaRegistryReader _registry;

    public async Task<JsonNode> ApplyAsync(string eventType, int storedVersion, int currentVersion, JsonNode payload)
    {
        var node = payload;
        for (var v = storedVersion; v < currentVersion; v++)
        {
            var definition = await _registry.GetVersionAsync(eventType, v + 1);
            if (definition.UpcastFromPrevious is { } compute)
                node = ComputeEvaluator.Evaluate(compute, node); // parses + evaluates "expr as alias, ..." via Microsoft.OData.UriParser
            // no upcastFromPrevious registered for this hop -- passed through as-is (ADR-018's accepted risk)
        }
        return node;
    }
}
```

Called from `FollowEndpoint` (per streamed event, before masking's
transform runs — masking operates on the *current* schema shape, so it
must see the upcasted payload, not the as-stored one) and from
`ProjectionHost` (per event, before `SnapshotMerger` — `09-cqrs-read-
models.md`, `ADR-016`), never from `LineageEndpoint` (which never
includes `Payload`).

**Also called from `PublishEndpoint` itself now (`ADR-020`)** — not to
transform what gets stored, but as a live validation pass:

```csharp
if (request.SchemaVersion < activeVersion)
{
    try { await upcastChain.ApplyAsync(eventType, request.SchemaVersion, activeVersion, request.Payload); }
    catch (UpcastFailedException ex)
    {
        return await appender.AppendAsync(BuildEventUpcastFailed(request, ex.FailedHop, ex.Reason));
    }
}
// upcast succeeded (or wasn't needed) -- store the ORIGINAL request.Payload at request.SchemaVersion, unchanged
return await appender.AppendAsync(BuildStoredEvent(request));
```

The upcasted result itself is discarded here — only whether it *succeeded*
matters at publish time; what actually gets stored is always the caller's
original, as-declared payload (`ADR-020`'s "on success, behavior is
otherwise unchanged").

## Auth: DPoP proof validation (`ADR-017`)

Alongside the existing JWT-bearer validation in
`AddEventStoreCommonServices`, a small middleware validates the `DPoP`
header on every request once the bearer token itself has already been
validated:

```csharp
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var proofValid = DPoPValidator.Validate(
            proofHeader: context.Request.Headers["DPoP"],
            httpMethod: context.Request.Method,
            httpUri: context.Request.GetEncodedUrl(),
            expectedJkt: context.User.FindFirstValue("cnf_jkt"), // from the access token's cnf.jkt claim
            presentedAccessToken: context.Request.Headers.Authorization);
        if (!proofValid)
        {
            await Results.Problem(statusCode: 401, type: "dpop-proof-invalid").ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});
```

`DevIdpSeeder` (`ADR-006`) generates and holds a key pair per seeded
client (`publisher-client`, `follower-client`, `operator-client`,
`projections-client`); the token endpoint embeds each client's `jwk`
thumbprint as `cnf.jkt` on every access token it issues to that client.

## Auth: dev identity provider (EventStore.DevIdp / OpenIddict) and local orchestration

For this POC, whichever `EventStore.Host.<Provider>` is deployed validates
Bearer JWTs against `EventStore.DevIdp`, a small in-process OpenIddict
host, rather than a production IdP (see `ADR-006`) — this is shared,
provider-agnostic auth wiring from `EventStore.Host.Core`
(`AddEventStoreCommonServices`), so it's identical across all three
deployables. `EventStore.DevIdp` is a plain ASP.NET Core project — not a
third-party container — so both orchestration paths below just run it like
any other project in the solution.

```csharp
// EventStore.DevIdp/Program.cs (sketch)
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DevIdpDbContext>(o => o.UseInMemoryDatabase("devidp"));
builder.Services.AddOpenIddict()
    .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<DevIdpDbContext>())
    .AddServer(o =>
    {
        o.SetTokenEndpointUris("/connect/token");
        o.AllowClientCredentialsFlow();
        o.RegisterScopes("events:publish", "events:follow", "events:lineage:read", "registry:admin");
        o.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
        o.UseAspNetCore().EnableTokenEndpointPassthrough();
    })
    .AddValidation(o => o.UseLocalServer().UseAspNetCore());

var app = builder.Build();
await DevIdpSeeder.SeedClientsAsync(app.Services); // publisher-client / follower-client / operator-client
app.MapDefaultEndpoints(); // OpenIddict exposes /.well-known/openid-configuration automatically
app.Run();
```

`DevIdpSeeder` is the single place the three clients and their scopes are
defined in code — see [`features/auth.md`](../docs/features/auth.md),
"Seeded clients (dev)", for the table it must match.

**`EventStore.AppHost` (.NET Aspire, preferred for local `dotnet run`/`aspire run`):**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddPostgres("db").WithDataVolume();
var devIdp = builder.AddProject<Projects.EventStore_DevIdp>("devidp"); // a project resource, not a container

// Per ADR-001, the AppHost targets exactly one Host.<Provider> project --
// swap which Projects.EventStore_Host_* type is referenced here to run
// locally against a different provider, there is no config value to flip.
builder.AddProject<Projects.EventStore_Host_Postgres>("eventstore")
    .WithReference(db)
    .WithReference(devIdp)
    .WithEnvironment("Authentication__Authority", devIdp.GetEndpoint("http"));

builder.Build().Run();
```

`EventStore.ServiceDefaults` wires the standard Aspire cross-cutting
concerns into whichever `EventStore.Host.<Provider>` is running (and
`EventStore.DevIdp`, `EventStore.Projections.Host`) via a single
`builder.AddServiceDefaults()` call — no lineage/auth logic lives there.
Per `ADR-026`, this is where all three OpenTelemetry signals — logging,
tracing, metrics — get configured, identically for every service in the
solution; see `ADR-026` for the full `ConfigureOpenTelemetry` code and
why health-check requests are filtered out of traces. Health checks and
Aspire service discovery are the other two things this project wires
here, not detailed further in this doc.

**`docker-compose.yml` (repo root, non-Aspire-tooling fallback):** two
ordinary app images — `eventstore` (built from whichever
`EventStore.Host.<Provider>` project matches the compose file's database
service, per `ADR-001` — not a generic image with a provider env var) and
`devidp` — plus that one database; `devidp` is built from the same
`EventStore.DevIdp` project, not pulled from a third-party registry, so
there's no external image or volume-mounted realm config to manage.
**`ADR-026` supersedes the framing below**: Aspire is for local
development only (it also wires all three OpenTelemetry signals —
logging, tracing, metrics — automatically via `ServiceDefaults`);
`docker-compose.yml` is the actual **production** deployment path, not a
CI/no-Aspire-CLI fallback.

Because `EventStore.DevIdp` uses an EF Core **InMemory** store, there is
nothing to import or persist — every fresh start re-seeds the three clients
from `DevIdpSeeder`. That is strictly less setup than Keycloak's
realm-export approach, at the cost of having no admin console to eyeball
the result (verify via a token request instead — see
[`features/auth.md`](../docs/features/auth.md)).

## Integration test strategy

`EventStore.IntegrationTests` parameterizes the same test suite across all
three providers using Testcontainers (Postgres, SQL Server) and an
in-memory/temp-file SQLite database, so pushdown-filter behavior is proven
identical on all three, not just unit-tested against mocks.

## CQRS read side (`EventStore.Projections.*`)

`ProjectionHost`'s DI wiring, `SnapshotMerger`, checkpoint/rebuild
mechanics, and `ProjectionsDbContext` are described in full in
`09-cqrs-read-models.md` — not repeated here. The one thing worth stating
at the solution-layout level: `EventStore.Projections.Host` references
none of `EventStore.Persistence`, `EventStore.Host.Core`, or any
`EventStore.Host.<Provider>` project. Its only dependency on the write
side is an HTTP client calling `QUERY /follow/{event-type}` — the same
public contract any external follower uses (`ADR-015`). This is enforced
by the project reference graph itself, not just a convention someone could
accidentally violate: there is no project reference to violate it with.

## Suggested References

- [EF Core](https://learn.microsoft.com/en-us/ef/core/) — DbContexts, migrations, both sides of the write/read split.
- [OpenIddict](https://openiddict.com/) — `EventStore.DevIdp`'s token issuer (`ADR-006`).
- [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview) — `EventStore.AppHost` orchestration.
- [Testcontainers](https://testcontainers.com/) — the integration test strategy across all three providers.
- [ASP.NET Core Minimal APIs — Routing](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis) — `MapMethods`, used for the `QUERY` routes (`ADR-012`).
- [RFC 9449](https://datatracker.ietf.org/doc/html/rfc9449) — DPoP, the proof-validation middleware sketched above (`ADR-017`).
- [Axon Framework — Event Versioning](https://docs.axoniq.io/axon-framework-reference/4.11/events/event-versioning/) — the upcaster-chain shape `UpcastChain` follows (`ADR-018`).

See `references.md` for the full bibliography.
