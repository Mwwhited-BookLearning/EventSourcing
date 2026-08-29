using EventStore.Abstractions;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EventStore.Erasure;

// ADR-096 -- orchestrates SearchIndexKey (provider-agnostic metadata) against
// whichever ISearchIndexKeyStore backend is configured, the same split
// ErasureKeyService already establishes for crypto-shredding DEKs. No
// bootstrap reserved-event-type registration here (unlike ErasureKeyService)
// -- a Shared-scope search-index key has no equivalent of
// EntityErasureRequested to guarantee exists first, since nothing ever
// "requests" destroying it.
public class SearchIndexKeyService(EventStoreContext db, IServiceProvider serviceProvider, IOptions<SearchIndexOptions> options)
{
    // First searchable field of a given (AppId, EventTypeName, FieldJsonPath)
    // published creates its key; every later value reuses the same key,
    // resolved via its already-recorded backend regardless of current config.
    public async Task<(string KeyReference, ISearchIndexKeyStore Backend)> GetOrCreateAsync(
        string appId, string eventTypeName, string fieldJsonPath, CancellationToken ct = default)
    {
        var existing = await db.SearchIndexKeys.SingleOrDefaultAsync(
            k => k.AppId == appId && k.EventTypeName == eventTypeName && k.FieldJsonPath == fieldJsonPath, ct);
        if (existing is not null)
            return (existing.KeyReference, ResolveBackend(existing.BackendName));

        var backendName = options.Value.DefaultBackend;
        var backend = ResolveBackend(backendName);
        var keyReference = await backend.CreateKeyAsync(appId, $"{eventTypeName}:{fieldJsonPath}", ct);

        db.SearchIndexKeys.Add(new SearchIndexKey
        {
            AppId = appId,
            EventTypeName = eventTypeName,
            FieldJsonPath = fieldJsonPath,
            KeyReference = keyReference,
            BackendName = backendName,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return (keyReference, backend);
    }

    // Read-only lookup for the query side (GraphQlFilterPredicateBuilder) --
    // null means no value of this field has ever been indexed yet, so no
    // query against it can possibly match anything.
    public async Task<(string KeyReference, ISearchIndexKeyStore Backend)?> ResolveAsync(
        string appId, string eventTypeName, string fieldJsonPath, CancellationToken ct = default)
    {
        var existing = await db.SearchIndexKeys.AsNoTracking().SingleOrDefaultAsync(
            k => k.AppId == appId && k.EventTypeName == eventTypeName && k.FieldJsonPath == fieldJsonPath, ct);
        return existing is null ? null : (existing.KeyReference, ResolveBackend(existing.BackendName));
    }

    private ISearchIndexKeyStore ResolveBackend(string backendName) =>
        serviceProvider.GetRequiredKeyedService<ISearchIndexKeyStore>(backendName);
}
