using EventStore.Erasure;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.IntegrationTests;

// Shared test wiring for "GDPR/CCPA Erasure via Crypto-Shredding"
// (docs/08-build-plan.md, ADR-057). Builds the publish-time (PayloadEncryptor)
// and read-time (IPayloadMasker) halves off the SAME db/registry instances
// the caller already constructed, and off the SAME ErasureKeyService/"Local"
// backend -- both LocalErasureKeyStore and EntityErasureKey are entirely
// EventStoreContext-table-backed (LocalErasureKeyStore's own comment: "a
// genuinely durable choice, not in-memory"), so a second, independently
// resolved ErasureKeyService pointed at the same db sees identical state --
// but sharing one container avoids two divergent SchemaRegistryService
// MemoryCache instances disagreeing about what's registered.
internal static class ErasureTestSupport
{
    public static (PayloadEncryptor Encryptor, IPayloadMasker Masker, ErasureKeyService KeyService) CreateErasureStack(
        EventStoreContext db, SchemaRegistryService registry)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton(registry);
        services.AddErasure(new ConfigurationBuilder().Build()); // no Erasure:Vault:Address -- "Local" backend only
        services.AddMasking(new Dictionary<string, string>()); // no Hash-strategy fields in these scenarios -- no HMAC key needed
        var provider = services.BuildServiceProvider();
        var keyService = provider.GetRequiredService<ErasureKeyService>();
        return (new PayloadEncryptor(keyService), provider.GetRequiredService<IPayloadMasker>(), keyService);
    }
}
