using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Masking;

public static class MaskingServiceCollectionExtensions
{
    // hmacKeys: keyId -> a base64-encoded, >= 32-byte secret (HmacRedactorOptions'
    // own validation requirement) -- one entry per x-masking.keyId this
    // deployment's registered schemas actually use; supports key rotation by
    // registering an old keyId alongside a new one, never removing an in-use
    // key outright. AddRedaction() is called even with an empty dictionary --
    // any classification with no explicit redactor configured (including every
    // "MaskingLogRedaction" classification PayloadMasker's own log-redaction
    // path uses) falls back to ErasingRedactor by default, never passing a real
    // value through unredacted.
    public static IServiceCollection AddMasking(this IServiceCollection services, IReadOnlyDictionary<string, string> hmacKeys)
    {
        services.AddKeyedSingleton<IMaskingStrategy, FixedValueMaskingStrategy>("FixedValue");
        services.AddKeyedSingleton<IMaskingStrategy, PartialRevealMaskingStrategy>("PartialReveal");
        services.AddKeyedSingleton<IMaskingStrategy, HashMaskingStrategy>("Hash");
        services.AddScoped<IPayloadMasker, PayloadMasker>();

        services.AddRedaction(redaction =>
        {
            foreach (var (keyId, secret) in hmacKeys)
                redaction.SetHmacRedactor(o => o.Key = secret, new DataClassification(HashMaskingStrategy.Taxonomy, keyId));
        });

        return services;
    }
}
