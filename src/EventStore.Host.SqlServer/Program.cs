using EventStore.Derivation;
using EventStore.Erasure;
using EventStore.ExpectedResponse;
using EventStore.FeatureFlags;
using EventStore.Flows;
using EventStore.Follow.Api;
using EventStore.GraphQL;
using EventStore.Interchange;
using EventStore.Host.Core;
using EventStore.Inbox;
using EventStore.Lineage.Api;
using EventStore.LineageExport;
using EventStore.Timestamping;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using EventStore.Rbac;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.SpecGeneration;
using EventStore.Archival;
using EventStore.Attachments;
using EventStore.Replication;
using EventStore.Streaming;
using EventStore.Upcasting;
using EventStore.Webhooks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(); // ADR-026 -- all three OTel signals, wired identically for every service
builder.AddEventStoreCommonServices();

// ADR-077 -- opt-in: only wired when FeatureFlags:AppId is configured, so
// every existing deployment/test with no such config section is
// completely unaffected. A raw ADO.NET connection, not EventStoreContext --
// this provider runs before WebApplicationBuilder.Build() (no DI container
// yet) and only ever reads one flat table with no JSON columns.
if (builder.Configuration["FeatureFlags:AppId"] is { } featureFlagsAppId)
{
    var sqlServerConnectionString = builder.Configuration.GetConnectionString("SqlServer");
    // ConfigurationManager (WebApplicationBuilder.Configuration's own type)
    // implements IConfigurationBuilder explicitly -- Add() isn't visible
    // without the cast.
    ((IConfigurationBuilder)builder.Configuration).Add(new EventLogFeatureFlagConfigurationSource(() => new SqlConnection(sqlServerConnectionString), featureFlagsAppId));
}

builder.Services.AddDbContext<EventStoreContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("SqlServer"),
    x => x.MigrationsAssembly("EventStore.Persistence.Migrations.SqlServer")));
// Read-only here -- the Samples.*.Flows worker hosts own writing/migrating
// this database. Always SQLite regardless of eventstore's own write-side
// provider (docs/09-cqrs-read-models.md's own "one EF Core provider is
// sufficient" posture, already established for OrdersProjectionsDbContext).
builder.Services.AddDbContext<PendingTasksDbContext>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("PendingTasks")));
builder.AddDbReachabilityHealthCheck(); // ADR-084 -- readiness fails when THIS database is unreachable
builder.Services.AddScoped<IJsonPathTranslator, SqlServerJsonPathTranslator>();
builder.Services.AddScoped<IFilterableFieldIndexDdlGenerator, SqlServerFilterableFieldIndexDdlGenerator>();
builder.Services.AddScoped<IUniqueConstraintViolationDetector, SqlServerUniqueConstraintViolationDetector>();
builder.Services.AddScoped<IEventLineageQueryProvider, SqlServerEventLineageQueryProvider>();
builder.Services.AddUpcasting(builder.Configuration);
builder.Services.AddSchemaRegistry();
builder.Services.AddFeatureFlags();
// ADR-095 -- SQL Server Service Broker, notify-to-wake/poll-to-confirm on
// top of RouterWorker's existing poll loop.
builder.Services.AddSqlServerWorkerWakeSignal();
builder.Services.AddInbox();
builder.Services.AddRouter();
builder.Services.AddDerivation();
builder.Services.AddSpecGeneration();
builder.Services.AddLineageApi();
builder.Services.AddLineageExport();
builder.Services.AddTimestamping(builder.Configuration);
builder.Services.AddErasure(builder.Configuration);
builder.Services.AddMasking(
    builder.Configuration.GetSection("Masking:HmacKeys").GetChildren().ToDictionary(c => c.Key, c => c.Value!));
builder.Services.AddFollowApi();
builder.Services.AddStreaming();
builder.Services.AddAttachments();
builder.Services.AddArchival();
builder.Services.AddReplication();
builder.Services.AddWebhooks();
builder.Services.AddExpectedResponseTracking();
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhooks"));
builder.Services.AddEventStoreGraphQl();
builder.Services.Configure<OriginIdOptions>(builder.Configuration.GetSection("OriginId"));
// SchemaRegistryOriginIdOptions duplicates OriginIdOptions' own shape/section
// rather than referencing it -- EventStore.SchemaRegistry can't reference
// EventStore.Inbox, which itself depends on EventStore.SchemaRegistry
// (ADR-033/090). Same "OriginId" section, so both resolve the same real
// per-site value.
builder.Services.Configure<SchemaRegistryOriginIdOptions>(builder.Configuration.GetSection("OriginId"));
builder.Services.Configure<PeerSyncOptions>(builder.Configuration.GetSection("PeerSync"));
builder.Services.Configure<PeerSyncClientOptions>(builder.Configuration.GetSection("PeerSyncClient"));
builder.Services.Configure<RegionOptions>(builder.Configuration.GetSection("Region"));
builder.Services.AddInterchange();
builder.Services.Configure<Hl7V2MllpOptions>(builder.Configuration.GetSection("Hl7V2Mllp"));
builder.Services.AddHttpClient("DevIdp", c => c.BaseAddress = new Uri(builder.Configuration["Authentication:Authority"]!));
builder.AddSpiffePeerIdentity(); // ADR-048 -- wires the "PeerSync" named HttpClient with this Host's own SVID; no fixed BaseAddress here either, same reason as before

var app = builder.Build();

// ADR-076 -- no replica ever calls Database.Migrate()/MigrateAsync() at
// startup (this line used to be here; two replicas starting concurrently
// against a fresh database is a known race). Schema is applied as a
// single deploy-time step BEFORE this Host starts, via an EF Core
// Migration Bundle -- see scripts/generate-migration-bundle.sh and
// scripts/apply-migration-bundle.sh for the local/POC equivalent of that
// deploy-time step (a real deployment pipeline would run the same bundle,
// sequenced ahead of every replica, per that ADR's own Consequences).

app.MapDefaultEndpoints();
app.MapEventStoreCommonEndpoints();
app.MapSchemaRegistryEndpoints();
app.MapPublishEndpoints();
app.MapRbacEndpoints();
app.MapFeatureFlagEndpoints();
app.MapDerivationEndpoints();
app.MapSpecGenerationEndpoints();
app.MapLineageEndpoints();
app.MapLineageExportEndpoints();
app.MapFollowEndpoints();
app.MapStreamingEndpoints();
app.MapAttachmentEndpoints();
app.MapPeerSyncEndpoints();
app.MapWebhookEndpoints();
app.MapGraphQlEndpoints();
app.MapInterchangeEndpoints();
app.Run();

public partial class Program;
