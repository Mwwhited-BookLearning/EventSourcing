using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EventStore.Persistence;

// EF Core's default model cache key is keyed by DbContext type only, not by which
// provider built the options -- a real problem for this design's single, portable
// EventStoreContext type, since OnModelCreating's DbFunction registrations close
// over an injected IJsonPathTranslator whose Translate() body differs per provider.
// Without this, the FIRST provider to build the model in a process would silently
// have its translator cached and reused for every other provider's context
// instances in the same run. Registered via EventStoreContext.OnConfiguring.
public sealed class ProviderAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), context.Database.ProviderName, designTime);
}
