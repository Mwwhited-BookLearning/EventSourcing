using Microsoft.AspNetCore.Builder;

namespace EventStore.Host.Core;

// Shared, provider-agnostic composition root (docs/06-solution-structure.md).
// Per ADR-001, exactly one thing varies per deployable -- the DbContext's
// provider registration (UseSqlite/UseNpgsql/UseSqlServer) plus the matching
// IJsonPathTranslator/migrations-assembly choice -- and that lives in each
// EventStore.Host.<Provider> project itself, not here. Everything else that
// doesn't vary by provider (auth, CORS, spec generation, endpoint mapping)
// is added here incrementally by the build-plan items that introduce it
// ("Auth + Orchestration" first) -- deliberately empty until then, not a
// placeholder for something that doesn't exist yet.
public static class HostCoreExtensions
{
    public static WebApplicationBuilder AddEventStoreCommonServices(this WebApplicationBuilder builder) => builder;

    public static WebApplication MapEventStoreCommonEndpoints(this WebApplication app) => app;
}
