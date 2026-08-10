# Solution Structure

**Propagation note, updated this session**: the project layout immediately
below reflects the post-integration shape (`ADR-021`–`039`) and now also
includes the `ADR-054`+ projects (Gateway/rate limiting, webhook
dispatcher, device-input client) that were missing. The detailed DI-wiring
code sketches further down this file have been brought current on naming
(`FollowEndpoint`/`LineageEndpoint` → the GraphQL Follow Subscription/
Lineage Query resolvers, `MapMethods(..., ["QUERY"], ...)`-per-surface →
`EventStore.GraphQL`'s single mapped endpoint, `RequiredPublishClaim`/
`RequiredReadClaim` → `RequiredClaims`) and the now-superseded
`AsyncApiDocumentBuilder`/`asyncapi.json` material is marked inline as
preserved-for-reference rather than current (`ADR-037`). `PublishEndpoint`
itself is unaffected by `ADR-037` and keeps its name. Not yet re-verified
line-by-line against every ADR past `ADR-041` (explicit composition) —
treat any code sketch not explicitly called out above with the same
"concept accurate, exact wiring unverified" caution as before.

**Propagation note, added building "SPIFFE/SPIRE Service Identity & API
Gateway" (`08-build-plan.md` item 24)**: the actual build consolidated
several of the separate deployables sketched below into fewer processes
rather than splitting one-project-per-service as literally drawn --
`EventStore.PeerSync` below was actually implemented as
`EventStore.Replication`, wired directly into each `EventStore.Host.
<Provider>` process rather than as its own deployable; the same is true
of `EventStore.Router`/`.Fold`/`.GraphQL`/`.Sharding`/`.Streaming`/
`.Attachments`, each a library namespace inside the same Host process,
not an independently-addressable service. `EventStore.Gateway` (YARP)
and `EventStore.Spiffe` (the SPIFFE ID/trust-bundle/mTLS primitives
`ADR-048` decided) **are** real, separate projects, matching this
sketch. Per `ADR-048`'s own Consequences ("each internal service project
[needs] its SPIFFE ID convention" annotated here): every SVID this
build actually issues follows `spiffe://<trust-domain>/eventstore/
<service-name>` exactly as decided, but since there is no real internal
network hop *between* Router/Fold/GraphQL/etc. in the actual build (they
share one process), SPIFFE/mTLS is only actually exercised at the two
genuine inter-process boundaries that exist: peer-to-peer sync between
independent site deployments, and the Gateway-to-Host hop this item
introduces -- both share one internal mTLS listener per Host
(`EventStore.Host.Core.SpiffePeerIdentity`/`SpiffePeerOptions.
AllowedInternalCallerPaths`), not one per sketched service below.
Reconciling this entire file's project list against what was actually
built, item by item, is a larger, separate cleanup -- tracked in
`TODO.md`, not attempted here.

**Deployment-unit note, added this session (`ADR-075`)**: this whole
solution builds to **one dedicated deployment per tenant** (the silo
model), not one shared deployment serving many tenants. `AppId` below
still scopes multiple *applications within one tenant's own deployment*
exactly as `ADR-030` describes — what changed is that a different
*customer* now means a wholly separate build/deploy of this same
solution, never a second `AppId` inside the same running instance.

## Project layout

```
EventStore.sln
  src/
    EventStore.Gateway/              -- YARP reverse proxy, the single external entry point (ADR-049); external TLS termination + ADR-006/017/040 auth happen here, handing off to ADR-048 SPIFFE/SPIRE workload identity internally; per-AppId rate limiting (Token Bucket for Inbox, Concurrency Limiter for GraphQL Subscriptions/Follow, Sliding Window for everything else, ADR-058) is enforced here first
    EventStore.Domain/              -- entities, no EF dependency; AppId is now part of every key (ADR-030)
    EventStore.Persistence/         -- DbContext, repositories, IJsonPathTranslator interface + all 3 impls
    EventStore.Persistence.Migrations.Sqlite/
    EventStore.Persistence.Migrations.Postgres/
    EventStore.Persistence.Migrations.SqlServer/
    EventStore.SchemaRegistry/      -- registration service, AppId-scoped lookups (ADR-030), ParentLinkService, upcast/downcast map validation (ADR-018/028); complex-case upcast mappings run sandboxed via Jint, common case via CEL (candidates only, see docs/libraries/dotnet/cel-dotnet.md)
    EventStore.Inbox/               -- POST /publish; Idempotent Receiver + always-202 append (ADR-011/023) -- the ONLY still-blocking-on-shape step is "can I parse the envelope at all"
    EventStore.Router/              -- background service: entity resolution (ADR-021), advisory schema/claim/authority checks (ADR-023/035), live upcast validation + materialization (ADR-020/027)
    EventStore.Fold/                -- background service: always-on Entity Store projector, logical-order fold (ADR-029), conflict flagging (ADR-024) -- distinct from opt-in custom projections below
    EventStore.GraphQL/             -- GraphQL Gateway: Query/Subscription, per-AppId schema (ADR-030/037), served via HotChocolate (docs/libraries/dotnet/hotchocolate.md); supersedes EventStore.Follow.Api/EventStore.Lineage.Api entirely
    EventStore.Sharding/            -- Shard Resolver: EntityId -> ShardKey -> store, entity-type-based (ADR-034)
    EventStore.PeerSync/            -- gossip peer-sync outbox/inbox, fault/abend/restart-tolerant (ADR-033)
    EventStore.Webhooks/            -- outbound webhook dispatcher: drains the durable WebhookOutbox (same fault/abend/restart-tolerant primitive as PeerSync/client outbox, ADR-033/039), Standard Webhooks HMAC signing, masks every payload against its subscription's fixed claim set before sending, exponential-backoff retry, dead-letters as WebhookDeliveryFailed on exhaustion (ADR-060)
    EventStore.Streaming/           -- TelemetryChannel/TelemetrySample ingestion + tail/replay, separate from the event pipeline entirely (ADR-031)
    EventStore.Attachments/         -- content-addressed binary storage; POST upload, GET with Range, browsable via the GraphQL Gateway (ADR-032)
    EventStore.InterchangeAdapters/  -- IInterchangeFormatAdapter seam + built-in adapters (Hl7V2Adapter, FhirAdapter, IchE2bR3Adapter, Gs1EpcisAdapter, ADR-072); inbound transforms publish through the ordinary Inbox path unchanged, outbound composes ahead of EventStore.Webhooks' delivery
    EventStore.InterchangeAdapters.Hl7v2MllpListener/  -- a dedicated background TCP listener speaking MLLP (ADR-072) -- HL7v2's real transport, unlike every other component in this solution (all HTTP/GraphQL); FHIR's own inbound path is ordinary HTTP, no separate listener needed. Transport security (TLS termination or network isolation) is the deploying team's own responsibility -- MLLP carries none itself
    EventStore.SpecGeneration/      -- OpenAPI builder (publish) + GraphQL SDL builder (supersedes the AsyncAPI builder for Follow -- Follow itself is gone, replaced by GraphQL Subscription); the two specs this project generates are what Kiota / GraphQL Code Generator / Strawberry Shake regenerate typed client SDKs from at consumer build time (ADR-054) -- no SDK-generation project lives in this solution itself, since generated code is never committed here, only in a consuming application's own build
    EventStore.Host.Core/           -- shared, provider-agnostic composition root logic (see below)
    EventStore.Host.Sqlite/         -- the actual deployable: Host.Core + SQLite wiring (ADR-001)
    EventStore.Host.Postgres/       -- the actual deployable: Host.Core + PostgreSQL wiring
    EventStore.Host.SqlServer/      -- the actual deployable: Host.Core + SQL Server wiring
    EventStore.DevIdp/              -- dev-only OpenIddict token issuer + OAuth Token Exchange for UCAN (ADR-006/036)
    EventStore.ServiceDefaults/     -- Aspire scaffolding: full OpenTelemetry (logging/tracing/metrics), health checks, service discovery (ADR-026)
    EventStore.AppHost/             -- Aspire orchestration for LOCAL DEV ONLY (ADR-026) -- production is docker-compose.yml, not this

    -- CQRS read side (09-cqrs-read-models.md, ADR-015/016) -- opt-in,
    -- custom projections built ON TOP of the always-on Entity Store
    -- (EventStore.Fold, above), a separate deployable, a separate
    -- database, talking to the write side only via the GraphQL Gateway
    -- like any other consumer:
    EventStore.Projections.Abstractions/  -- IProjection<T>, ChangeKind-agnostic; projection authors depend on only this
    EventStore.Projections.Host/          -- ProjectionHost, SnapshotMerger (Optional<T>-aware, ADR-022), ProjectionsDbContext

    -- MVVM client (ADR-039) -- consumes the framework, doesn't extend it:
    EventStore.Client.Core/               -- ViewModel base types, ICommandDispatcher, client-local durable outbox/inbox (same fault-tolerance bar as EventStore.PeerSync)
    EventStore.Client.WebViewBridge/      -- native<->HTML+JS bridge (WebView2/WKWebView/CEF), hosts the web app below
    EventStore.Client.DeviceInput/        -- IDeviceInputSource seam (docs/extensibility-points.md) + NativeBridgeInputSource, the local companion app exposing a localhost WebSocket/HTTP server for Firefox/Safari or any device interface none of the four browser APIs below reach (ADR-070); captured readings feed EventStore.Client.Core's outbox unchanged
    client-web/                           -- npm workspace, NOT a .NET project: Vue 3 + Pinia + Naive UI application shell
                                           -- (docs/patterns/mvvm-client-architecture.md, docs/libraries/web/*.md); built to
                                           -- static assets, loaded by WebViewBridge for the native shell and served directly
                                           -- for the browser/PWA target -- one build artifact, two hosts
                                           -- also hosts WebUsbInputSource/WebHidInputSource/WebSerialInputSource/WebBluetoothInputSource
                                           -- (ADR-070) -- WebUSB/WebHID/Web Serial/Web Bluetooth are browser-only APIs, reachable
                                           -- only from this open page/window context, Chromium-only for 3 of the 4 (ADR-070)
    client-web/offline-player/            -- NOT a separate app -- an alternate Vite build target of client-web's own
                                           -- lineage/playback Vue component (ADR-068), configured with vite-plugin-singlefile
                                           -- (docs/libraries/web/vite-plugin-singlefile.md) to inline all JS/CSS into one
                                           -- self-contained .html file: data + playback UI both embedded, zero external
                                           -- requests, opens by double-click, no server, no install. Self-verifying on load
                                           -- (independently recomputes the ADR-019 hash chain + ADR-068 manifest hash from
                                           -- the embedded bundle) -- see docs/features/lineage-export-and-playback.md

    -- Sample application, explicitly NOT part of the framework (ADR-030):
    Samples.Orders.Projections/           -- worked example: OrderSummaryProjection (features/cqrs-projections.md)
  tests/
    EventStore.UnitTests/            -- MSTest + Moq (ADR-055)
    EventStore.IntegrationTests/    -- runs against all three providers (see below)
    EventStore.E2ETests/             -- Playwright, MSTest base classes -- drives the Vue client through a real browser (ADR-055)
    EventStore.Bdd/                 -- Reqnroll/SpecFlow-style step definitions for *.feature files
```

Frontend unit tests (`Vitest` + `Vue Test Utils`, `ADR-055`) live inside
`EventStore.Client.Vue/` itself, alongside the components they test, not
under `tests/` — matching how that project is already its own top-level
solution area, not a subfolder of the write-side services above.

`EventStore.Follow.Api` and `EventStore.Lineage.Api` (the OData-era
projects) no longer exist as separate projects — `ADR-037` folds both
into `EventStore.GraphQL`; the underlying traversal/tailing logic they
contained is unchanged, only its transport and query-argument syntax
moved. `EventStore.Publish.Api` is renamed `EventStore.Inbox` and split
from a new `EventStore.Router`, reflecting `ADR-023`'s inbox/router
separation — persistence and understanding are no longer the same step.

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
        // ODataFilterParser removed, ADR-037 -- HotChocolate's [UseFiltering] middleware resolves
        // the GraphQL `where` argument natively; there is no separate parser this project owns anymore.
        builder.Services.AddProblemDetails(); // ADR-013: one error shape, every endpoint (Publish only -- GraphQL uses its own partial-success error shape, 03-api-contracts.md)
        builder.Services.AddMemoryCache(); // backs the ~60s spec-document cache, ADR-002
        builder.Services.AddSingleton<EventSchemaConverter>();      // JsonSchema text -> shared Microsoft.OpenApi OpenApiSchema
        builder.Services.AddSingleton<MaskingSchemaTransformer>();  // schema-level x-masking -> oneOf[value,masked,erased] wrapper (ADR-057)
        builder.Services.AddSingleton<OpenApiDocumentBuilder>();
        // AsyncApiDocumentBuilder removed, ADR-037 -- Follow's spec is now GraphQL SDL, served by
        // HotChocolate itself; see the "Spec generation" section below for the superseded detail.

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
            .AddPolicy("registry:admin", p => p.Requirements.Add(new ScopeRequirement("registry:admin")));
            // events:follow / events:lineage:read are no longer ASP.NET route policies -- Follow and
            // Lineage are GraphQL Subscription/Query resolvers now (ADR-037), authorized via
            // HotChocolate's own [Authorize] directive on the schema, resolved against the same
            // ScopeRequirement handler at the GraphQL Gateway (EventStore.GraphQL), not a Minimal API policy.
    }

    public static void MapEventStoreCommonEndpoints(this WebApplication app)
    {
        app.UseCors("EventStoreCors"); // ADR-014 -- before endpoint mapping, applies to all of them
        // /publish, /openapi.json -- Follow, Lineage, and registry listing are GraphQL resolvers now
        // (ADR-037), mapped separately by EventStore.GraphQL's own MapGraphQL() call, not here.
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

> **`AsyncApiDocumentBuilder`/`asyncapi.json` below is superseded, `ADR-037`.**
> Follow moved from an AsyncAPI-described SSE endpoint to a GraphQL
> Subscription (still delivered over SSE via the `graphql-sse` protocol —
> only the document/query language changed, not the transport). HotChocolate
> serves the GraphQL SDL itself (its own built-in schema-introspection/SDL
> endpoint), so there is no second, hand-built spec document for Follow to
> generate — `EventStore.SpecGeneration` keeps only the OpenAPI half
> (`OpenApiDocumentBuilder`, publish-side, unaffected by `ADR-037`). The code
> below is preserved as a description of the pre-`ADR-037` mechanism, not
> the current one — see `03-api-contracts.md` for the current GraphQL SDL
> story.

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

`RequiredClaims` (`ADR-008`, generalized from a single `RequiredPublishClaim`/
`RequiredReadClaim` field to an `OR`-matched list by `ADR-050`) can't be
wired as static ASP.NET Core policies the way the four scopes are: a
policy's requirement is fixed at startup, but which claims are required
depends on *which event type* the request names, which is only known once
the route value is bound (Publish) or the resolver's `EventType` argument
is resolved (GraphQL) and the registry is queried. So this check is plain
application code, run after the `EventTypeDefinition` is loaded — not a
declarative `[Authorize(Policy = "...")]`:

```csharp
static bool HasRequiredClaim(ClaimsPrincipal user, string requiredClaim)
{
    var (type, value) = SplitOnce(requiredClaim, ':');
    return user.HasClaim(type, value); // a single discrete claim -- the built-in check is fine here,
}                                       // unlike ScopeRequirement's space-delimited-claim problem above

// Called against a RequiredClaims list for a given Direction, OR-matched (ADR-050):
static bool HasAnyRequiredClaim(ClaimsPrincipal user, IReadOnlyList<RequiredClaim> requiredClaims, ClaimDirection direction)
{
    var forDirection = requiredClaims.Where(c => c.Direction == direction).ToList();
    return forDirection.Count == 0 || forDirection.Any(c => HasRequiredClaim(user, c.Claim));
}
```

Called from `PublishEndpoint` (against a `Publish`-direction
`RequiredClaims` entry, after resolving the active `EventTypeDefinition`,
before schema validation — `ADR-050` generalized this from a single
`RequiredPublishClaim` to a list, `OR`-matched) and the GraphQL
Gateway's Follow **Subscription resolver** (against a `Read`-direction
`RequiredClaims` entry for its own event type, once at connect time,
alongside `[UseFiltering]`'s `where`-field validation) exactly as a
single pass/fail check. The Lineage **Query resolver** uses it
differently, per `ADR-008`'s "you can only see what you can see": once
for the root `eventId`'s own type (pass/fail, `403` if it fails — you
can't query the lineage of something you can't see), then again,
independently, for every *other* distinct `EventType` the traversal
discovers — a failure there doesn't reject the request, it turns that
one node into a `restricted: true` stub (see `03-api-contracts.md`)
without affecting any other node in the response.

```csharp
// Lineage Query resolver (illustrative): build the restricted-type set once,
// same HasAnyRequiredClaim primitive as the root check, then consult it per discovered node
var restrictedTypes = allEventTypesInResult
    .Where(t => !HasAnyRequiredClaim(user, t.RequiredClaims, ClaimDirection.Read))
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

**Which strategy computes `masked`'s content is a Strategy-pattern seam
(`ADR-009`), not a `switch` inside the recursion above:**

```csharp
public interface IMaskingStrategy
{
    // Pure per call: the "masked" branch's content, from the real value
    // and this leaf's x-masking config. No I/O, no ambient state.
    JsonNode Mask(JsonNode realValue, JsonObject maskingConfig);
}

public sealed class FixedValueMaskingStrategy : IMaskingStrategy { /* maskedValue, default "***" */ }
public sealed class PartialRevealMaskingStrategy : IMaskingStrategy { /* showFirst/showLast/maskChar/preserveSeparators */ }

public sealed class HashMaskingStrategy(IRedactorProvider redactorProvider) : IMaskingStrategy
{
    // Delegates to Microsoft.Extensions.Compliance.Redaction's HmacRedactor
    // (ADR-050) -- keyed by maskingConfig["keyId"] -- not a bare hash.
}
```

Registered in the explicit composition root, one keyed line per strategy
(`ADR-041` — no reflection-based auto-discovery):

```csharp
services.AddKeyedSingleton<IMaskingStrategy, FixedValueMaskingStrategy>("FixedValue");
services.AddKeyedSingleton<IMaskingStrategy, PartialRevealMaskingStrategy>("PartialReveal");
services.AddKeyedSingleton<IMaskingStrategy, HashMaskingStrategy>("Hash");
```

`PayloadMasker` (the `IPayloadMasker` implementation) takes an
`IServiceProvider` in its constructor, used *only* to resolve
`IServiceProvider.GetRequiredKeyedService<IMaskingStrategy>(leaf.Strategy)`
per masked leaf as the recursion walks the schema — the one deliberate,
narrow exception to `ADR-041`'s "no service-locator lookups reached for
from inside arbitrary code" rule, since *which* implementation applies is
a runtime fact (the `strategy` string sitting in registered schema data),
not something a compile-time constructor parameter could express. This is
exactly the scenario .NET's keyed-service resolution API exists for, not
a workaround. Adding a fourth strategy (e.g. a future generalization/
bucketing option, `docs/comparisons/masking-strategies.md`) is a new
`IMaskingStrategy` class plus one registration line — `PayloadMasker`
itself never changes.

Because it's a pure `(schema, data) -> data` step with claim-checking
injected, it composes as a link in a small command chain rather than logic
embedded in the Follow Subscription resolver specifically:

```csharp
// Follow Subscription resolver's per-event pipeline (illustrative):
var maskedPayload = payloadMasker.Mask(activeSchema, rawPayload, claimType => user.HasClaim(...));
```

The *set* of claims to check is fixed for the life of one Follow connection
(same JWT throughout — the connection is now a GraphQL Subscription over
`graphql-sse`, `ADR-037`, but the claim-set-fixed-for-connection-lifetime
rule is unchanged from before that move), so `hasClaim` can close over a
claim set computed once at connect time — but the masker itself doesn't
know or care that that's how its caller chose to supply it. A future direct
"read event by id" resolver reuses `IPayloadMasker` unchanged; only the
surrounding pipeline (a GraphQL Query resolver for a discrete request, or
an explicit per-event step for a long-lived Subscription like Follow)
differs per transport. The stored `Payload` is never touched by any of
this — masking is computed fresh at the response boundary, for whichever
caller is asking.

Per-deployment build is the accepted v1 approach (three artifacts, one per
provider, no runtime config value) — see `ADR-001`. Each
`EventStore.Host.<Provider>` passes its own migrations assembly directly to
`UseSqlite`/`UseNpgsql`/`UseSqlServer` (shown in the DI wiring section
above) — there's no assembly-selection logic to get wrong at startup,
because each deployable only ever has one to choose from.

## Routing `QUERY` — one GraphQL endpoint, not one `MapMethods` per surface (`ADR-012`/`ADR-037`)

There is no longer a per-surface `MapMethods("/follow/{eventType}", ["QUERY"], ...)`
route, or a separate one for each Lineage/registry-listing path — `ADR-037`
folded Follow, Lineage, and registry listing into one GraphQL endpoint,
`EventStore.GraphQL`, mapped once. What survives from the OData era is
*which HTTP method* that one endpoint travels over: HotChocolate's
`MapGraphQL()` defaults to `POST`, but `ADR-037` keeps the query surface on
`QUERY` (`ADR-012`'s original reasoning — a query document's arguments can
carry PII/PHI, and `QUERY`'s body-carrying, cacheable-but-safe semantics
keep that out of URLs/access logs/proxy caches the way `GET` never could).
Getting HotChocolate to serve over `QUERY` instead of its default `POST`
needs a small custom endpoint mapping, not a config flag — see
`docs/libraries/dotnet/hotchocolate.md` for the concrete integration note.
Mutations stay `POST` (they have side effects regardless of PII, so
`QUERY`'s safety guarantee doesn't apply to them).

```csharp
// EventStore.GraphQL/Program.cs (illustrative) -- one endpoint, not one MapMethods call per surface
app.MapGraphQL(); // then re-mapped/wrapped to accept QUERY instead of POST -- see hotchocolate.md
```

There is no request-body-reading step for this design to own anymore
either — HotChocolate parses the GraphQL document (query text + variables)
out of the request body itself; `ADR-003`'s old per-field `$filter`/`$top`/
`$skip` string-reading is gone along with the OData surface it belonged to.

## Follow: tail vs replay cursor (`ADR-010`)

The Follow **Subscription resolver** runs one continuous poll loop
(`04-odata-filter-pushdown.md`: `WHERE SequenceNumber > lastSeen AND
predicate`, translated now from a GraphQL `where` argument via
`[UseFiltering]` rather than a parsed OData AST — see that document for the
current pipeline) regardless of `mode` — only how `lastSeen` is
*initialized*, once, at connect time, differs:

```csharp
long lastSeen = mode switch
{
    FollowMode.Tail   => await db.Events.MaxAsync(e => (long?)e.SequenceNumber) ?? 0,
    FollowMode.Replay => fromSequenceNumber ?? 0,
    _ => throw new UnreachableException()
};
// then the existing poll loop runs unchanged from here, for either mode
```

`fromSequenceNumber` is rejected before this point if `mode != Replay` —
validated alongside the `where`-argument's field validation, same place
`RequiredClaims` (`Read` direction) is checked (`ADR-008`). GraphQL surfaces
this rejection through its own error shape, not a bare `400` (`03-api-
contracts.md`). No new persistence, no per-consumer state: the caller
supplies the cursor on every connection; it isn't remembered server-side
between connections.

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
class to write. **Corrected in this pass — the previous version of this
section was wrong**: it described `upcastFromPrevious` as an OData
`compute()` expression evaluated via `Microsoft.OData.UriParser`, but
`ADR-037` explicitly moved upcast mapping *off* OData `compute()` — that
choice originally justified itself by reusing the OData parser `$filter`
already needed, and with OData gone from the query surface entirely, that
reuse argument no longer holds. `upcastFromPrevious` is now an
engine-agnostic expression *string* — **data**, not code — evaluated by a
pluggable `IUpcastExpressionEvaluator` (`ADR-053`; `UpcastChain` itself,
`ADR-018`, depends only on the interface, resolved via the explicit
composition root, `ADR-041`): **CEL by default** for the common
declarative case, **JSONata** a documented, swappable alternative
(neither array-aggregation vs. maturity trade-off forces a permanent
choice), and sandboxed **Jint** as the separate, always-available escape
hatch for the rare complex case neither declarative engine covers. See
`docs/comparisons/upcast-transform-language.md` and
`docs/libraries/dotnet/cel-dotnet.md`/`jint.md` for the full reasoning;
`04-odata-filter-pushdown.md`'s "Historical" section is *not* what this
mechanism uses anymore — that section is preserved for the unrelated
`$filter` surface only, don't confuse the two:

```csharp
public class UpcastChain
{
    private readonly ISchemaRegistryReader _registry;
    private readonly IUpcastExpressionEvaluator _evaluator; // resolved via DI, ADR-053 -- CEL by default

    public async Task<JsonNode> ApplyAsync(string eventType, int storedVersion, int currentVersion, JsonNode payload)
    {
        var node = payload;
        for (var v = storedVersion; v < currentVersion; v++)
        {
            var definition = await _registry.GetVersionAsync(eventType, v + 1);
            if (definition.UpcastFromPrevious is { } expression)
                node = await _evaluator.EvaluateAsync(expression, node); // engine-agnostic text in, transformed JsonNode out
            // no upcastFromPrevious registered for this hop -- passed through as-is (ADR-018's accepted risk)
        }
        return node;
    }
}
```

Called from the Follow Subscription resolver (per streamed event, before
masking's transform runs — masking operates on the *current* schema shape,
so it must see the upcasted payload, not the as-stored one) and from
`ProjectionHost` (per event, before `SnapshotMerger` — `09-cqrs-read-
models.md`, `ADR-016`), never from the Lineage Query resolver (which never
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

## Data lifecycle — what to back up (`ADR-056`)

Quick-reference for whoever sizes a backup plan, so it isn't
reconstructed from first principles across three separate data-model
docs. Re-checked against the actual current set of `EventStoreContext`
`DbSet`s while building "Data Lifecycle & Backup/Restore Classification"
(`08-build-plan.md` item 25) — five tables this section had not yet
named (`DerivationDefinition`/`DerivationCursor`/`PendingJoinState`,
added by "Derived/Materialized Event Types"; `PeerSyncCursor`, added by
"Sharding & Replication"; `ViewDefinition`, added by "Entity-Centric Core
Rebuild") are folded in below, per this item's own dependency text
("its coverage of specific stores grows accurate as [more items] land").

**Authoritative, must back up**:
- Event Log + `EventParent` (`event-log.md`), Schema Registry
  (`schema-registry.md`), Streaming Channel Store (`streaming-and-
  attachments.md`), Attachment Store (same), Read Access Audit Log
  (`ADR-045`), and `ADR-057`'s `EntityErasureKey` metadata (`entity-
  store.md` — losing it doesn't prevent *future* erasure, but does lose
  the mapping needed to *request* one for an already-existing entity
  without consulting the external key store's own listing; not yet
  built as of this item, "GDPR/CCPA Erasure via Crypto-Shredding" is the
  re-check trigger named below).
- `DerivationDefinition` and `PendingJoinState` (`schema-registry.md`,
  `ADR-007`, deferred) — a registered derivation is admin-configured
  metadata, the same class as `EventTypeDefinition` itself; nothing
  recomputes it from the Event Log. `PendingJoinState` is a FireOnce
  join's own in-flight, not-yet-completed state — losing it silently
  drops whichever sources had already arrived for that join key, and no
  existing mechanism replays history to reconstruct which joins were
  pending at the moment of loss.
- `DerivationCursor` (same doc/ADR) — `DerivationWorker.ProcessDerivationAsync`
  skips a source entirely when its cursor row is missing (`cursors.
  TryGetValue` returning false, not "start from 0"); losing this row
  silently and permanently stops that derivation consuming that source,
  not merely a slower resync. Grouped with the two above rather than
  called "rebuildable," since no code path currently regenerates it.
- `ViewDefinition` (`entity-store.md`, "Entity-Centric Core Rebuild") —
  admin-configured metadata, the same class as `EventTypeDefinition`;
  nothing recomputes a registered view's shape from other data.

**Rebuildable, backup optional** — Entity Store, every CQRS read model/
snapshot, materialized upcasts: all recoverable by re-running the
existing fold/rebuild mechanism (`ADR-021`/`ADR-015`) against a restored
authoritative store, verified end to end for `EntityStoreRow`/
`LiveEntityStoreRow` specifically by this item's own restore-drill test
(`DataLifecycleScenarioAssertions`) — wipe both tables, reset every
`StoredEvent.Status` back to `"received"`, re-run `RouterWorker.
RunOnceAsync` (the same public entry point the live worker already
uses, no separate rebuild-only code path), and the reconstructed rows
match the pre-wipe state field for field. `PeerSyncCursor` (`ADR-033`,
`schema-registry.md`) belongs here too, on the same reasoning: losing it
only costs a slower resync (`ADR-033`'s peers naturally re-exchange
already-acked events, its own idempotency absorbing the resend) —
backup optional, a pure RTO trade, not a data-loss risk.

Nothing about `ADR-004`'s portable-column choice blocks a provider's
native backup/PITR tooling from working against any of the above.

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
- [ASP.NET Core Minimal APIs — Routing](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis) — `MapMethods`, still used for `PublishEndpoint` (`ADR-012`); Follow/Lineage/registry listing route through `EventStore.GraphQL`'s single endpoint instead (`ADR-037`).
- [HotChocolate — Server Endpoints](https://chillicream.com/docs/hotchocolate/server/endpoints) — `MapGraphQL()` and the custom mapping needed to serve over `QUERY` instead of its default `POST` (`ADR-012`/`ADR-037`, `docs/libraries/dotnet/hotchocolate.md`).
- [RFC 9449](https://datatracker.ietf.org/doc/html/rfc9449) — DPoP, the proof-validation middleware sketched above (`ADR-017`).
- [Axon Framework — Event Versioning](https://docs.axoniq.io/axon-framework-reference/4.11/events/event-versioning/) — the upcaster-chain shape `UpcastChain` follows (`ADR-018`).

See `references.md` for the full bibliography.
