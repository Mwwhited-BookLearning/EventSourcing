using EventStore.Derivation;
using EventStore.Erasure;
using EventStore.FeatureFlags;
using EventStore.Follow.Api;
using EventStore.GraphQL;
using EventStore.Host.Core;
using EventStore.Inbox;
using EventStore.Lineage.Api;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.Rbac;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.SpecGeneration;
using EventStore.Attachments;
using EventStore.Replication;
using EventStore.Streaming;
using EventStore.Upcasting;
using EventStore.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

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
    var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
    // ConfigurationManager (WebApplicationBuilder.Configuration's own type)
    // implements IConfigurationBuilder explicitly -- Add() isn't visible
    // without the cast.
    ((IConfigurationBuilder)builder.Configuration).Add(new EventLogFeatureFlagConfigurationSource(() => new NpgsqlConnection(postgresConnectionString), featureFlagsAppId));
}

builder.Services.AddDbContext<EventStoreContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Postgres"),
    x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Postgres")));
builder.Services.AddScoped<IJsonPathTranslator, PostgresJsonPathTranslator>();
builder.Services.AddScoped<IFilterableFieldIndexDdlGenerator, PostgresFilterableFieldIndexDdlGenerator>();
builder.Services.AddScoped<IUniqueConstraintViolationDetector, PostgresUniqueConstraintViolationDetector>();
builder.Services.AddScoped<IEventLineageQueryProvider, PostgresEventLineageQueryProvider>();
builder.Services.AddUpcasting();
builder.Services.AddSchemaRegistry();
builder.Services.AddFeatureFlags();
builder.Services.AddInbox();
builder.Services.AddRouter();
builder.Services.AddDerivation();
builder.Services.AddSpecGeneration();
builder.Services.AddLineageApi();
builder.Services.AddErasure(builder.Configuration);
builder.Services.AddMasking(
    builder.Configuration.GetSection("Masking:HmacKeys").GetChildren().ToDictionary(c => c.Key, c => c.Value!));
builder.Services.AddFollowApi();
builder.Services.AddStreaming();
builder.Services.AddAttachments();
builder.Services.AddReplication();
builder.Services.AddWebhooks();
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhooks"));
builder.Services.AddEventStoreGraphQl();
builder.Services.Configure<OriginIdOptions>(builder.Configuration.GetSection("OriginId"));
builder.Services.Configure<PeerSyncOptions>(builder.Configuration.GetSection("PeerSync"));
builder.Services.Configure<PeerSyncClientOptions>(builder.Configuration.GetSection("PeerSyncClient"));
builder.Services.AddHttpClient("DevIdp", c => c.BaseAddress = new Uri(builder.Configuration["Authentication:Authority"]!));
builder.AddSpiffePeerIdentity(); // ADR-048 -- wires the "PeerSync" named HttpClient with this Host's own SVID; no fixed BaseAddress here either, same reason as before

var app = builder.Build();

// Applies pending migrations against whatever database this deployment
// points at (Aspire's freshly-provisioned container, docker-compose's
// volume, or an existing one) -- found missing by actually running this
// Host under `aspire run` against a brand-new Postgres container, which
// had no schema at all and 500'd on first request.
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<EventStoreContext>().Database.MigrateAsync();

app.MapDefaultEndpoints();
app.MapEventStoreCommonEndpoints();
app.MapSchemaRegistryEndpoints();
app.MapPublishEndpoints();
app.MapRbacEndpoints();
app.MapFeatureFlagEndpoints();
app.MapDerivationEndpoints();
app.MapSpecGenerationEndpoints();
app.MapLineageEndpoints();
app.MapFollowEndpoints();
app.MapStreamingEndpoints();
app.MapAttachmentEndpoints();
app.MapPeerSyncEndpoints();
app.MapWebhookEndpoints();
app.MapGraphQlEndpoints();
app.Run();

public partial class Program;
