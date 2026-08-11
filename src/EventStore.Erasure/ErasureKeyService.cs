using EventStore.Domain.EntityStore;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EventStore.Erasure;

// Orchestrates EntityErasureKey (provider-agnostic metadata, this project's
// main DB) against whichever IErasureKeyStore backend this AppId is
// configured for (ADR-057) -- the backends themselves know nothing about
// EntityErasureKey or AppId-to-backend selection, only "create/encrypt/
// decrypt/destroy under this reference."
public class ErasureKeyService(EventStoreContext db, IServiceProvider serviceProvider, IOptions<ErasureOptions> options, SchemaRegistryService schemaRegistry)
{
    // First classified field published for this entity creates its DEK;
    // every later one reuses the same key, resolved via its already-recorded
    // backend regardless of what AppId's current configuration says now.
    public async Task<(string KeyReference, IErasureKeyStore Backend)> GetOrCreateAsync(
        string appId, string entityId, CancellationToken ct = default)
    {
        // Guarantees EntityErasureRequested exists for this AppId before any
        // encrypted data does -- so an erasure request for this AppId is
        // always publishable once it could ever be meaningful.
        await EntityErasureRequestedEventType.EnsureRegisteredAsync(schemaRegistry, appId, ct);

        var existing = await db.EntityErasureKeys.SingleOrDefaultAsync(k => k.EntityId == entityId, ct);
        if (existing is not null)
            return (existing.KeyReference, ResolveBackend(existing.BackendName));

        var backendName = options.Value.BackendFor(appId);
        var backend = ResolveBackend(backendName);
        var keyReference = await backend.CreateKeyAsync(appId, entityId, ct);

        db.EntityErasureKeys.Add(new EntityErasureKey
        {
            EntityId = entityId,
            KeyReference = keyReference,
            BackendName = backendName,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return (keyReference, backend);
    }

    // null means no classified field has ever been published for this
    // entity -- there is nothing to decrypt or erase, distinct from
    // Erased=true (a key that existed and was destroyed).
    public async Task<(string KeyReference, IErasureKeyStore Backend, bool Erased)?> ResolveAsync(
        string entityId, CancellationToken ct = default)
    {
        var existing = await db.EntityErasureKeys.AsNoTracking().SingleOrDefaultAsync(k => k.EntityId == entityId, ct);
        return existing is null ? null : (existing.KeyReference, ResolveBackend(existing.BackendName), existing.ErasedAt is not null);
    }

    // Idempotent -- an entity with no key on file, or one already erased,
    // is left alone rather than treated as an error.
    public async Task EraseAsync(string entityId, CancellationToken ct = default)
    {
        var existing = await db.EntityErasureKeys.SingleOrDefaultAsync(k => k.EntityId == entityId, ct);
        if (existing is null || existing.ErasedAt is not null)
            return;

        var backend = ResolveBackend(existing.BackendName);
        await backend.DestroyKeyAsync(existing.KeyReference, ct);
        existing.ErasedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private IErasureKeyStore ResolveBackend(string backendName) =>
        serviceProvider.GetRequiredKeyedService<IErasureKeyStore>(backendName);
}
