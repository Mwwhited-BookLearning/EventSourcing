using System.Security.Claims;
using System.Text.Json.Nodes;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using HotChocolate;
using Microsoft.EntityFrameworkCore;

namespace EventStore.GraphQL;

// docs/03-api-contracts.md, "revealField -- explicit reveal-on-demand
// (ADR-009, ADR-050)": the actual reveal-on-demand round trip Property-Level
// Masking's own build-plan item could only build half of (it built
// displayMask, not this mutation, since GraphQL didn't exist yet). Builds
// the CORE mechanism this item's own exit criteria actually names ("a
// follower calling revealField on a masked node it holds the claim for
// receives the real value, and the same call without the claim is
// rejected") -- ADR-066's optional step-up-authentication refinement for a
// field configured to require one, and the ADR-045 AccessLogEntry audit
// write, are NOT built here: both depend on infrastructure two later,
// not-yet-built build-plan items own (RFC 9470 step-up enforcement,
// "Digital Sign-Off for Regulated Actions"; the AccessLogEntry table
// itself, "Delegated Grants, RBAC, Federated Claims & Read Audit
// Logging") -- honestly flagged in 08-build-plan.md rather than stubbed.
[ExtendObjectType(OperationTypeNames.Mutation)]
public class RevealFieldMutation
{
    [GraphQLName("revealField")]
    public async Task<RevealFieldPayload> RevealFieldAsync(
        string entityId, Guid eventId, string fieldPath,
        [Service] ClaimsPrincipal user, EventStoreContext db, SchemaRegistryService schemaRegistry, CancellationToken ct)
    {
        var storedEvent = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == eventId, ct)
            ?? throw new GraphQLException("Unknown eventId.");
        if (storedEvent.EntityId != entityId)
            throw new GraphQLException("entityId does not match the stored event's own EntityId.");

        var definition = await schemaRegistry.GetVersionAsync(storedEvent.AppId, storedEvent.EventType, storedEvent.SchemaVersion, ct)
            ?? throw new GraphQLException("No registered schema found for this event's own declared version.");

        if (!JsonPathValidation.IsSafe(fieldPath))
            throw new GraphQLException("fieldPath must be a simple dotted-identifier chain (e.g. \"$.SubjectNationalId\").");
        var segments = JsonPathValidation.Segments(fieldPath);

        var schemaNode = JsonNode.Parse(definition.JsonSchema);
        var fieldSchema = NavigateSchema(schemaNode, segments)
            ?? throw new GraphQLException("fieldPath does not resolve against this event's own registered schema.");

        if (fieldSchema["x-masking"] is not JsonObject maskingConfig || maskingConfig["requiredClaim"]?.GetValue<string>() is not { } requiredClaim)
            throw new GraphQLException("fieldPath does not name a maskable field -- nothing to reveal.");

        if (!RequiredClaimEvaluator.HasClaim(user, requiredClaim))
            throw new GraphQLException("Forbidden -- caller lacks the required claim to reveal this field.");

        var payloadNode = JsonNode.Parse(storedEvent.Payload);
        var valueNode = NavigatePayload(payloadNode, segments);
        var value = valueNode switch
        {
            null => null,
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v => v.ToJsonString(),
            _ => valueNode.ToJsonString(),
        };
        return new RevealFieldPayload(value);
    }

    private static JsonNode? NavigateSchema(JsonNode? schemaNode, IReadOnlyList<string> segments)
    {
        var current = schemaNode;
        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || obj["properties"] is not JsonObject properties ||
                !properties.TryGetPropertyValue(segment, out var next) || next is null)
                return null;
            current = next;
        }
        return current;
    }

    private static JsonNode? NavigatePayload(JsonNode? payloadNode, IReadOnlyList<string> segments)
    {
        var current = payloadNode;
        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out var next))
                return null;
            current = next;
        }
        return current;
    }
}

public record RevealFieldPayload(string? Value);
