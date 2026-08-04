using EventStore.Domain.Streaming;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Streaming;

// ADR-052 -- resolves the right keyed IStreamRedactionStrategy. Unlike
// EventStore.Masking's IPayloadMasker (where the x-masking config names its
// own strategy key directly), RedactedRange.Strategy "Default" doesn't name
// one fixed class -- it means "whichever substitution ADR-052 designates
// for this channel's own ContentKind/MimeType" -- so this resolver, not the
// caller, picks the concrete key.
public class StreamRedactionResolver(IServiceProvider services)
{
    public IStreamRedactionStrategy Resolve(TelemetryChannel channel, RedactedRange range)
    {
        var key = range.Strategy switch
        {
            "PartialReveal" => "PartialReveal",
            _ => channel.ContentKind switch
            {
                ContentKind.Media when channel.MimeType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true => "Tone",
                ContentKind.Media when channel.MimeType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true => "BlankFrame",
                _ => "ZeroFill",
            },
        };
        return services.GetRequiredKeyedService<IStreamRedactionStrategy>(key);
    }
}
