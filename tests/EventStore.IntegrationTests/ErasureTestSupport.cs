using EventStore.Abstractions;
using EventStore.Erasure;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.IntegrationTests;

// Shared test wiring for "GDPR/CCPA Erasure via Crypto-Shredding"
// (docs/08-build-plan.md, ADR-057) and, since ADR-096/097, the searchable-
// index seams AddErasure now also registers. Builds the publish-time
// (PayloadEncryptor/PayloadIndexer) and read-time (IPayloadMasker) halves
// off the SAME db/registry instances the caller already constructed, and
// off the SAME ErasureKeyService/"Local" backend -- both LocalErasureKeyStore
// and EntityErasureKey are entirely EventStoreContext-table-backed
// (LocalErasureKeyStore's own comment: "a genuinely durable choice, not
// in-memory"), so a second, independently resolved ErasureKeyService
// pointed at the same db sees identical state -- but sharing one container
// avoids two divergent SchemaRegistryService MemoryCache instances
// disagreeing about what's registered.
internal static class ErasureTestSupport
{
    public static (PayloadEncryptor Encryptor, IPayloadMasker Masker, ErasureKeyService KeyService) CreateErasureStack(
        EventStoreContext db, SchemaRegistryService registry)
    {
        var provider = BuildProvider(db, registry);
        var keyService = provider.GetRequiredService<ErasureKeyService>();
        return (provider.GetRequiredService<PayloadEncryptor>(), provider.GetRequiredService<IPayloadMasker>(), keyService);
    }

    // ADR-096/097 -- a separate accessor rather than widening
    // CreateErasureStack's own tuple (which ~20 existing call sites already
    // destructure positionally; changing its arity would break every one of
    // them for no benefit to tests that don't touch searchable indexing).
    // Same underlying container -- AddErasure registers both halves
    // together -- so a test can call both accessors against the same
    // db/registry and get consistent, shared state.
    public static (PayloadIndexer Indexer, SearchIndexKeyService SearchIndexKeyService, IEncryptedPredicateEvaluator PredicateEvaluator) CreateSearchIndexStack(
        EventStoreContext db, SchemaRegistryService registry)
    {
        var provider = BuildProvider(db, registry);
        return (provider.GetRequiredService<PayloadIndexer>(), provider.GetRequiredService<SearchIndexKeyService>(), provider.GetRequiredService<IEncryptedPredicateEvaluator>());
    }

    private static ServiceProvider BuildProvider(EventStoreContext db, SchemaRegistryService registry)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton(registry);
        services.AddErasure(new ConfigurationBuilder().Build()); // no Erasure:Vault:Address -- "Local" backend only
        services.AddMasking(new Dictionary<string, string>()); // no Hash-strategy fields in these scenarios -- no HMAC key needed
        return services.BuildServiceProvider();
    }
}
