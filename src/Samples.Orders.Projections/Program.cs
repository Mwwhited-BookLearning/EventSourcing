using EventStore.Projections.Host;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Samples.Orders.Projections;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient("Follow", c => c.BaseAddress = new Uri(builder.Configuration["Follow:BaseAddress"]!));
builder.Services.AddHttpClient("DevIdp", c => c.BaseAddress = new Uri(builder.Configuration["DevIdp:BaseAddress"]!));
builder.Services.Configure<FollowClientOptions>(builder.Configuration.GetSection("Follow:Client"));
builder.Services.Configure<ProjectionHostOptions>(builder.Configuration.GetSection("Projections"));
builder.Services.AddSingleton<FollowClient>(); // resolved once by ProjectionHost<T>'s own singleton-lifetime BackgroundService constructor

builder.Services.AddOrdersProjections(builder.Configuration.GetConnectionString("OrdersProjections")!);

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<OrdersProjectionsDbContext>().Database.MigrateAsync();

await app.RunAsync();
