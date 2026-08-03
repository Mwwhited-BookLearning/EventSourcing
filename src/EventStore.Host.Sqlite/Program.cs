using EventStore.Derivation;
using EventStore.Follow.Api;
using EventStore.Host.Core;
using EventStore.Inbox;
using EventStore.Lineage.Api;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using EventStore.SpecGeneration;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(); // ADR-026 -- all three OTel signals, wired identically for every service
builder.AddEventStoreCommonServices();

builder.Services.AddDbContext<EventStoreContext>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("Sqlite"),
    x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite")));
builder.Services.AddScoped<IJsonPathTranslator, SqliteJsonPathTranslator>();
builder.Services.AddScoped<IFilterableFieldIndexDdlGenerator, SqliteFilterableFieldIndexDdlGenerator>();
builder.Services.AddScoped<IUniqueConstraintViolationDetector, SqliteUniqueConstraintViolationDetector>();
builder.Services.AddScoped<IEventLineageQueryProvider, SqliteEventLineageQueryProvider>();
builder.Services.AddSchemaRegistry();
builder.Services.AddInbox();
builder.Services.AddDerivation();
builder.Services.AddSpecGeneration();
builder.Services.AddLineageApi();
builder.Services.AddFollowApi();

var app = builder.Build();

// Applies pending migrations against whatever database this deployment
// points at (Aspire's freshly-provisioned container, docker-compose's
// volume, or an existing one) -- found missing by actually running the
// Postgres Host under `aspire run` against a brand-new container, which
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
app.Run();

public partial class Program;
