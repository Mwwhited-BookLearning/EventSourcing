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
builder.Services.AddSpecGeneration();
builder.Services.AddLineageApi();
builder.Services.AddFollowApi();

var app = builder.Build();
app.MapEventStoreCommonEndpoints();
app.MapSchemaRegistryEndpoints();
app.MapPublishEndpoints();
app.MapSpecGenerationEndpoints();
app.MapLineageEndpoints();
app.MapFollowEndpoints();
app.Run();
