using EventStore.Derivation;
using EventStore.Follow.Api;
using EventStore.Host.Core;
using EventStore.Inbox;
using EventStore.Lineage.Api;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.SpecGeneration;
using EventStore.Attachments;
using EventStore.Replication;
using EventStore.Streaming;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(); // ADR-026 -- all three OTel signals, wired identically for every service
builder.AddEventStoreCommonServices();

builder.Services.AddDbContext<EventStoreContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Postgres"),
    x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Postgres")));
builder.Services.AddScoped<IJsonPathTranslator, PostgresJsonPathTranslator>();
builder.Services.AddScoped<IFilterableFieldIndexDdlGenerator, PostgresFilterableFieldIndexDdlGenerator>();
builder.Services.AddScoped<IUniqueConstraintViolationDetector, PostgresUniqueConstraintViolationDetector>();
builder.Services.AddScoped<IEventLineageQueryProvider, PostgresEventLineageQueryProvider>();
builder.Services.AddUpcasting();
builder.Services.AddSchemaRegistry();
builder.Services.AddInbox();
builder.Services.AddRouter();
builder.Services.AddDerivation();
builder.Services.AddSpecGeneration();
builder.Services.AddLineageApi();
builder.Services.AddMasking(
    builder.Configuration.GetSection("Masking:HmacKeys").GetChildren().ToDictionary(c => c.Key, c => c.Value!));
builder.Services.AddFollowApi();
builder.Services.AddStreaming();
builder.Services.AddAttachments();
builder.Services.AddReplication();
builder.Services.Configure<OriginIdOptions>(builder.Configuration.GetSection("OriginId"));
builder.Services.Configure<PeerSyncOptions>(builder.Configuration.GetSection("PeerSync"));
builder.Services.Configure<PeerSyncClientOptions>(builder.Configuration.GetSection("PeerSyncClient"));
builder.Services.AddHttpClient("DevIdp", c => c.BaseAddress = new Uri(builder.Configuration["Authentication:Authority"]!));
builder.Services.AddHttpClient("PeerSync"); // no fixed BaseAddress -- PeerSyncClient dials each peer's own absolute address

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
app.MapDerivationEndpoints();
app.MapSpecGenerationEndpoints();
app.MapLineageEndpoints();
app.MapFollowEndpoints();
app.MapStreamingEndpoints();
app.MapAttachmentEndpoints();
app.MapPeerSyncEndpoints();
app.Run();

public partial class Program;
