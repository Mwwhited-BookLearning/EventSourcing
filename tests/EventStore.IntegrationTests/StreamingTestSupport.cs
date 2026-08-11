using EventStore.Streaming;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.IntegrationTests;

// Shared test wiring for "Streaming Channels" (docs/08-build-plan.md) --
// StreamRedactionResolver resolves its keyed IStreamRedactionStrategy
// instances through IServiceProvider, the same real composition-root
// pattern MaskingTestSupport already establishes for IPayloadMasker,
// rather than hand-constructing the resolver against a container that
// doesn't actually have the keyed registrations it depends on.
internal static class StreamingTestSupport
{
    public static (StreamRedactionResolver Resolver, IServiceProvider Provider) CreateRedactionResolver()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IStreamRedactionStrategy, ZeroFillStrategy>("ZeroFill");
        services.AddKeyedSingleton<IStreamRedactionStrategy, ToneStrategy>("Tone");
        services.AddKeyedSingleton<IStreamRedactionStrategy, BlankFrameStrategy>("BlankFrame");
        services.AddKeyedSingleton<IStreamRedactionStrategy, PartialRevealStreamRedactionStrategy>("PartialReveal");
        services.AddSingleton<StreamRedactionResolver>();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<StreamRedactionResolver>(), provider);
    }
}
