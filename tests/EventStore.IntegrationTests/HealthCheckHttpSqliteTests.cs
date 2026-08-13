using System.Net;
using EventStore.Persistence;
using EventStore.Host.Core;
using EventStore.Persistence.Migrations.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// ADR-084's own addendum -- both halves of this item's TODO.md entry,
// proven directly rather than assumed from reading the code: a real
// DB-reachability check participates in "/health" (readiness) but not
// "/alive" (liveness), and both endpoints are reachable outside
// Development now, not just in it. No auth needed here at all -- these
// endpoints are deliberately unauthenticated, the same posture almost
// every real production Kubernetes deployment already takes for its own
// probes.
[TestClass]
public class HealthCheckHttpSqliteTests
{
    private static string _dbPath = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-health-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using var db = new EventStoreContext(options, new SqliteJsonPathTranslator());
        await db.Database.MigrateAsync();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment, string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Sqlite", connectionString);
        });

    [TestMethod]
    public async Task HealthAndAliveAreReachableInProductionNotJustDevelopment()
    {
        using var factory = CreateFactory(Environments.Production, $"Data Source={_dbPath}");
        using var client = factory.CreateClient();

        using var health = await client.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode, "readiness must be reachable in Production, not gated to Development");
        Assert.AreEqual("Healthy", await health.Content.ReadAsStringAsync());

        using var alive = await client.GetAsync("/alive");
        Assert.AreEqual(HttpStatusCode.OK, alive.StatusCode, "liveness must be reachable in Production, not gated to Development");
        Assert.AreEqual("Healthy", await alive.Content.ReadAsStringAsync());
    }

    // Deliberately NOT an HTTP/WebApplicationFactory test like the one
    // above -- a database unreachable from the very first moment the
    // process starts doesn't reach the "/health returns 503" scenario at
    // all, confirmed directly, not assumed: HotChocolate's own AspNetCore.
    // Warmup.RequestExecutorWarmupService eagerly builds the GraphQL
    // schema as a blocking IHostedService (FollowSubscriptionTypeModule.
    // CreateTypesAsync queries the database directly), BEFORE any request
    // -- including a health check itself -- can ever be served, so an
    // unreachable-from-startup database crashes the whole
    // WebApplicationFactory.CreateClient() call outright instead. That's
    // arguably correct for that exact scenario (ADR-084's own Decision
    // text groups "primary database unreachable" alongside "an
    // unrecoverable startup failure," not as a merely-not-ready one), but
    // it means proving AddDbReachabilityHealthCheck's own readiness/
    // liveness split needs to bypass the full Host app (and its
    // HotChocolate warmup) entirely -- a minimal HostApplicationBuilder
    // with just EventStoreContext + the health check registered,
    // resolving IHealthCheckService directly, the same DI-level testing
    // HostCoreExtensions' own callers rely on this method actually doing.
    [TestMethod]
    public async Task ReadinessFailsWhenThePrimaryDatabaseIsUnreachableButLivenessDoesNot()
    {
        var unreachablePath = Path.Combine(Path.GetTempPath(), $"eventstore-health-missing-dir-{Guid.NewGuid():N}", "nope.db");
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<EventStoreContext>(options => options.UseSqlite(
            $"Data Source={unreachablePath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite")));
        builder.Services.AddScoped<IJsonPathTranslator, SqliteJsonPathTranslator>();
        builder.AddDbReachabilityHealthCheck();
        // The same "self" liveness check MapDefaultEndpoints' own "/alive"
        // filters to -- registered here directly (not via
        // AddDefaultHealthChecks, which also pulls in the full
        // OpenTelemetry/service-discovery wiring this minimal builder
        // doesn't need) so the "live"-tag Predicate below has something
        // real to filter FOR, matching the Host's own actual "/alive" set.
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        await using var provider = builder.Services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();

        // "/health" (readiness) -- no predicate, every registered check
        // (self AND the DB-reachability one) must pass.
        var readiness = await healthCheckService.CheckHealthAsync();
        Assert.AreEqual(HealthStatus.Unhealthy, readiness.Status, "ADR-084 -- readiness fails when THIS instance's own primary database is unreachable");

        // "/alive" (liveness) -- MapDefaultEndpoints' own Predicate, r =>
        // r.Tags.Contains("live") -- excludes the untagged DB check
        // entirely, so the same unreachable database that fails readiness
        // above must NOT also fail liveness, ADR-084's own Decision.
        var liveness = await healthCheckService.CheckHealthAsync(r => r.Tags.Contains("live"));
        Assert.AreEqual(HealthStatus.Healthy, liveness.Status, "liveness must never fail because of a dependency's health, ADR-084's own Decision");
    }
}
