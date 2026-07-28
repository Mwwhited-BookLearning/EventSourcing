# Solution Structure

## Project layout

```
EventStore.sln
  src/
    EventStore.Domain/              -- entities, no EF dependency
    EventStore.Persistence/         -- DbContext, repositories, IJsonPathTranslator + impls
    EventStore.Persistence.Migrations.Sqlite/
    EventStore.Persistence.Migrations.Postgres/
    EventStore.Persistence.Migrations.SqlServer/
    EventStore.SchemaRegistry/      -- registration service, validation, ParentLinkService
    EventStore.Publish.Api/         -- POST /publish/{event-type}
    EventStore.Follow.Api/          -- GET /follow/{event-type} (SSE), OData parsing
    EventStore.Lineage.Api/         -- GET /events/{id}/parents|children|ancestors|descendants
    EventStore.SpecGeneration/      -- OpenAPI + AsyncAPI builders
    EventStore.Host/                -- composition root: DI wiring, appsettings, Program.cs
    EventStore.DevIdp/              -- dev-only OpenIddict token issuer, in-process (see below)
    EventStore.ServiceDefaults/     -- Aspire scaffolding: OpenTelemetry, health checks, service discovery defaults
    EventStore.AppHost/             -- Aspire orchestration for local dev/POC (see below)
  tests/
    EventStore.UnitTests/
    EventStore.IntegrationTests/    -- runs against all three providers (see below)
    EventStore.Bdd/                 -- Reqnroll/SpecFlow-style step definitions for *.feature files
```

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
`dotnet ef migrations add <Name> --project src/EventStore.Persistence.Migrations.Sqlite --startup-project src/EventStore.Host` and equivalent per provider.

## DI wiring (composition root)

```csharp
var provider = builder.Configuration["Database:Provider"]!;

builder.Services.AddDbContext<EventStoreContext>(options => provider switch
{
    "Sqlite"    => options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite")),
    "Postgres"  => options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")),
    "SqlServer" => options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer")),
    _ => throw new InvalidOperationException($"Unknown provider '{provider}'")
});

builder.Services.AddScoped<IJsonPathTranslator>(sp => provider switch
{
    "Sqlite"    => new SqliteJsonPathTranslator(),
    "Postgres"  => new PostgresJsonPathTranslator(),
    "SqlServer" => new SqlServerJsonPathTranslator(),
    _ => throw new InvalidOperationException($"Unknown provider '{provider}'")
});

builder.Services.AddScoped<ISchemaRegistryReader, SchemaRegistryReader>();
builder.Services.AddScoped<SchemaValidationService>();
builder.Services.AddScoped<ParentLinkService>();
builder.Services.AddScoped<ODataFilterParser>();
builder.Services.AddScoped<IEventLineageQueryProvider>(sp => provider switch
{
    "Sqlite"    => new SqliteEventLineageQueryProvider(),
    "Postgres"  => new PostgresEventLineageQueryProvider(),
    "SqlServer" => new SqlServerEventLineageQueryProvider(),
    _ => throw new InvalidOperationException($"Unknown provider '{provider}'")
});
builder.Services.AddMemoryCache(); // backs the ~60s spec-document cache, ADR-002
builder.Services.AddSingleton<EventSchemaConverter>();      // JsonSchema text -> shared Microsoft.OpenApi OpenApiSchema
builder.Services.AddSingleton<MaskingSchemaTransformer>();  // schema-level x-masking -> oneOf[value,masked] wrapper
builder.Services.AddSingleton<OpenApiDocumentBuilder>();
builder.Services.AddSingleton<AsyncApiDocumentBuilder>();

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
```

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

Both endpoints are thin routes in `EventStore.Host`:

```csharp
app.MapGet("/openapi.json", async (OpenApiDocumentBuilder b) =>
    Results.Text(await b.GetOrBuildJsonAsync(), "application/json")).AllowAnonymous();
app.MapGet("/asyncapi.json", async (AsyncApiDocumentBuilder b) =>
    Results.Text(await b.GetOrBuildJsonAsync(), "application/json")).AllowAnonymous();
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
resolving the active `EventTypeDefinition`, before schema validation),
`FollowEndpoint` (against `RequiredReadClaim`, once at connect time,
alongside the `$filter`-field validation), and `LineageEndpoint` (against
`RequiredReadClaim` for every distinct `EventType` present across the
result set — including the root `{eventId}`'s own type — failing the whole
response with `403` if any check fails; see `03-api-contracts.md`,
"RequiredReadClaim and the Lineage API").

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

Runtime provider switch is the recommended v1 approach (single deployable
artifact; `Database:Provider` in `appsettings.json` or environment
variable) — see `ADR-001`. Startup must apply pending migrations for the
active provider's migration assembly only.

```csharp
var migrationsAssembly = provider switch
{
    "Sqlite"    => "EventStore.Persistence.Migrations.Sqlite",
    "Postgres"  => "EventStore.Persistence.Migrations.Postgres",
    "SqlServer" => "EventStore.Persistence.Migrations.SqlServer",
    _ => throw new InvalidOperationException()
};
// pass migrationsAssembly into the relevant options.UseSqlite/UseNpgsql/UseSqlServer(..., x => x.MigrationsAssembly(migrationsAssembly))
```

## Event lineage (parent/child DAG) queries

`parents`/`children` are plain LINQ joins against `EventParents` — fully
portable, no raw SQL needed. `ancestors`/`descendants` need a recursive
query; EF Core's LINQ provider has no translation for recursive CTEs, so
these are the one query path in the store that isn't a pure `IQueryable`:

```csharp
public interface IEventLineageQueryProvider
{
    Task<IReadOnlyList<LineageNode>> GetAncestorsAsync(Guid eventId);
    Task<IReadOnlyList<LineageNode>> GetDescendantsAsync(Guid eventId);
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

## Auth: dev identity provider (EventStore.DevIdp / OpenIddict) and local orchestration

For this POC, `EventStore.Host` validates Bearer JWTs against
`EventStore.DevIdp`, a small in-process OpenIddict host, rather than a
production IdP (see `ADR-006`). `EventStore.DevIdp` is a plain ASP.NET Core
project — not a third-party container — so both orchestration paths below
just run it like any other project in the solution.

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

var db = builder.AddPostgres("db").WithDataVolume(); // swap for AddSqlServer(...) per Database:Provider
var devIdp = builder.AddProject<Projects.EventStore_DevIdp>("devidp"); // a project resource, not a container

builder.AddProject<Projects.EventStore_Host>("eventstore")
    .WithReference(db)
    .WithReference(devIdp)
    .WithEnvironment("Authentication__Authority", devIdp.GetEndpoint("http"));

builder.Build().Run();
```

`EventStore.ServiceDefaults` wires the standard Aspire cross-cutting
concerns (OpenTelemetry, health checks, service discovery) into
`EventStore.Host` (and `EventStore.DevIdp`) via `builder.AddServiceDefaults()`
— no lineage/auth logic lives there.

**`docker-compose.yml` (repo root, non-Aspire-tooling fallback):** two
ordinary app images — `eventstore` and `devidp` — plus the chosen database;
`devidp` is built from the same `EventStore.DevIdp` project, not pulled from
a third-party registry, so there's no external image or volume-mounted
realm config to manage. CI or anyone without the Aspire CLI gets an
identical dev environment either way. Aspire is preferred day-to-day
because it also wires telemetry/health checks automatically; compose stays
as the lowest-common-denominator path.

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
