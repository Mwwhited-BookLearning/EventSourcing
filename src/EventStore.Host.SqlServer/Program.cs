using EventStore.Host.Core;
using EventStore.Inbox;
using EventStore.Lineage.Api;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.SqlServer;
using EventStore.SchemaRegistry;
using EventStore.SpecGeneration;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddEventStoreCommonServices();

builder.Services.AddDbContext<EventStoreContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("SqlServer"),
    x => x.MigrationsAssembly("EventStore.Persistence.Migrations.SqlServer")));
builder.Services.AddScoped<IJsonPathTranslator, SqlServerJsonPathTranslator>();
builder.Services.AddScoped<IFilterableFieldIndexDdlGenerator, SqlServerFilterableFieldIndexDdlGenerator>();
builder.Services.AddScoped<IUniqueConstraintViolationDetector, SqlServerUniqueConstraintViolationDetector>();
builder.Services.AddScoped<IEventLineageQueryProvider, SqlServerEventLineageQueryProvider>();
builder.Services.AddSchemaRegistry();
builder.Services.AddInbox();
builder.Services.AddSpecGeneration();
builder.Services.AddLineageApi();

var app = builder.Build();
app.MapEventStoreCommonEndpoints();
app.MapSchemaRegistryEndpoints();
app.MapPublishEndpoints();
app.MapSpecGenerationEndpoints();
app.MapLineageEndpoints();
app.Run();
