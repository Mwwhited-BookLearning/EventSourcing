using EventStore.Masking;
using Microsoft.Extensions.Logging;

namespace EventStore.Inbox;

// ADR-050 -- the STATIC log-redaction shape (see ActorIdentityAttribute's
// own header comment for how this differs from PayloadMasker's dynamic
// one): [ActorIdentity] on actorId is what makes the source-generated
// logging call redact it automatically via whatever Redactor is
// registered for ActorIdentityTaxonomy.Name, rather than writing the
// caller's real identity into plaintext logs. A real, motivated call
// site -- PublishService.PublishAsync logging exactly who was rejected
// and why is a genuinely useful security diagnostic (an operator
// investigating a spike in rejected publishes needs this), the concrete
// "e.g. logging a ClientId" case this ADR's own Decision text names.
internal static partial class PublishServiceLogMessages
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Publish of {EventType} rejected for actor {ActorId}: {Reason}")]
    public static partial void PublishRejected(this ILogger logger, string eventType, [ActorIdentity] string actorId, string reason);
}
