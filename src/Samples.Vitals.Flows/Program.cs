using EventStore.Flows;
using EventStore.Projections.Host;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Samples.Vitals;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient("Follow", c => c.BaseAddress = new Uri(builder.Configuration["Follow:BaseAddress"]!));
builder.Services.AddHttpClient("DevIdp", c => c.BaseAddress = new Uri(builder.Configuration["DevIdp:BaseAddress"]!));
builder.Services.Configure<FollowClientOptions>(builder.Configuration.GetSection("Follow:Client"));
builder.Services.Configure<ProjectionHostOptions>(builder.Configuration.GetSection("Projections"));
builder.Services.AddSingleton<FollowClient>(); // resolved once by ProjectionHost<T>'s own singleton-lifetime BackgroundService constructor

builder.Services.AddFlowEngine(builder.Configuration.GetConnectionString("PendingTasks")!);
builder.Services.AddFlow(VitalsWorkflowBFlow.Build());
builder.Services.AddFlow(VitalsWorkflowDFlow.Build());

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<PendingTasksDbContext>().Database.MigrateAsync();

await app.RunAsync();
