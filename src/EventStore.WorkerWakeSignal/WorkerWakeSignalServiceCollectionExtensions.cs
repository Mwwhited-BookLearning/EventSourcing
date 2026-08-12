using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStore.WorkerWakeSignal;

public static class WorkerWakeSignalServiceCollectionExtensions
{
    // Scoped, matching EventStoreContext's own natural lifetime (a
    // singleton here couldn't legally depend on a scoped EventStoreContext
    // at all) -- the actual cross-scope shared state that makes a
    // publisher's NotifyAsync reach a worker's own long-lived
    // WaitForWakeAsync loop lives in SqliteWorkerWakeSignal's own `static`
    // fields, not in this wrapper instance, so a fresh scoped instance per
    // call site still shares the identical Channel/observed-signal state.
    public static IServiceCollection AddSqliteWorkerWakeSignal(this IServiceCollection services)
    {
        services.TryAddScoped<IWorkerWakeSignal, SqliteWorkerWakeSignal>();
        return services;
    }
}
