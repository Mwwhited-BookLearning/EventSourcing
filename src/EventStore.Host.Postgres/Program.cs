using EventStore.Host.Core;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.SchemaRegistry;
using EventStore.SpecGeneration;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddEventStoreCommonServices();

builder.Services.AddDbContext<EventStoreContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Postgres"),
    x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Postgres")));
builder.Services.AddScoped<IJsonPathTranslator, PostgresJsonPathTranslator>();
builder.Services.AddScoped<IFilterableFieldIndexDdlGenerator, PostgresFilterableFieldIndexDdlGenerator>();
builder.Services.AddScoped<IUniqueConstraintViolationDetector, PostgresUniqueConstraintViolationDetector>();
builder.Services.AddSchemaRegistry();
builder.Services.AddInbox();
builder.Services.AddSpecGeneration();

var app = builder.Build();
app.MapEventStoreCommonEndpoints();
app.MapSchemaRegistryEndpoints();
app.MapPublishEndpoints();
app.MapSpecGenerationEndpoints();
app.Run();
