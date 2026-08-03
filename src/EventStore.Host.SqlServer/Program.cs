using EventStore.Host.Core;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddEventStoreCommonServices();

builder.Services.AddDbContext<EventStoreContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("SqlServer"),
    x => x.MigrationsAssembly("EventStore.Persistence.Migrations.SqlServer")));
builder.Services.AddScoped<IJsonPathTranslator, SqlServerJsonPathTranslator>();
builder.Services.AddScoped<IFilterableFieldIndexDdlGenerator, SqlServerFilterableFieldIndexDdlGenerator>();
builder.Services.AddSchemaRegistry();

var app = builder.Build();
app.MapEventStoreCommonEndpoints();
app.MapSchemaRegistryEndpoints();
app.Run();
