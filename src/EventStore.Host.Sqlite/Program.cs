using EventStore.Host.Core;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddEventStoreCommonServices();

builder.Services.AddDbContext<EventStoreContext>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("Sqlite"),
    x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite")));
builder.Services.AddScoped<IJsonPathTranslator, SqliteJsonPathTranslator>();
builder.Services.AddScoped<IFilterableFieldIndexDdlGenerator, SqliteFilterableFieldIndexDdlGenerator>();
builder.Services.AddSchemaRegistry();

var app = builder.Build();
app.MapEventStoreCommonEndpoints();
app.MapSchemaRegistryEndpoints();
app.Run();
