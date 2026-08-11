using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace EventStore.Domain.EventLog;

// ADR-011's canonical { eventType, payload, parentEventIds: <sorted> } hash.
// Shared by PublishService (computes it once at insert time) and
// ChainVerificationService (recomputes it from the stored row's own
// EventType/Payload/parent links to detect a direct-database Payload edit
// that left the PayloadHash column itself untouched -- ADR-019's own
// "altering any past Payload/PayloadHash breaks every subsequent ChainHash"
// promise only holds if verification re-derives PayloadHash from Payload
// rather than trusting the stored column blindly).
public static class EventPayloadHash
{
    public static string Compute(string eventType, string payloadJson, IReadOnlyList<Guid> parentEventIds)
    {
        var canonical = new JsonObject
        {
            ["eventType"] = eventType,
            ["payload"] = JsonNode.Parse(payloadJson),
            ["parentEventIds"] = new JsonArray(parentEventIds.OrderBy(id => id).Select(id => (JsonNode)id.ToString()).ToArray()),
        };
        var bytes = Encoding.UTF8.GetBytes(canonical.ToJsonString());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
