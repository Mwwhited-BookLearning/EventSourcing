using EventStore.WorkerWakeSignal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStore.Persistence.Migrations.SqlServer;

public static class SqlServerWorkerWakeSignalServiceCollectionExtensions
{
    public static IServiceCollection AddSqlServerWorkerWakeSignal(this IServiceCollection services)
    {
        services.TryAddScoped<IWorkerWakeSignal, SqlServerWorkerWakeSignal>();
        return services;
    }
}
