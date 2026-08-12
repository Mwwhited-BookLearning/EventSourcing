using EventStore.WorkerWakeSignal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStore.Persistence.Migrations.Postgres;

public static class PostgresWorkerWakeSignalServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresWorkerWakeSignal(this IServiceCollection services)
    {
        services.TryAddScoped<IWorkerWakeSignal, PostgresWorkerWakeSignal>();
        return services;
    }
}
