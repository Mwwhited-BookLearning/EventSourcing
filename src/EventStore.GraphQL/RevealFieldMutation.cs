using System.Security.Claims;
using System.Text.Json.Nodes;
using EventStore.Domain.AccessLog;
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
// rejected"), plus the ADR-045 AccessLogEntry audit write (Action: "reveal",
// per that ADR's own consequences section -- "revealField's own field-level
// grain gives sharper audit than an ordinary bulk query already has").
//
// ADR-066's optional step-up-authentication refinement -- built, later
// pass: an `x-masking` field can carry its own `requiredSignature`
// (`{ "acrValues": [...], "maxAge": ... }`, the same shape/field names
// EventTypeDefinition.RequiredSignature already uses for publish-time
// enforcement, ADR-066's original Decision), checked via the SAME
// StepUpEvaluator (EventStore.Domain.SchemaRegistry) PublishService uses --
// extracted there specifically so this mutation doesn't duplicate that
// logic. A field with no `requiredSignature` is completely unaffected.
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

        // ADR-043 -- an entity-scoped grant (a delegated "secondary
        // opinion" claim, restricted to one specific EntityId) passes here
        // only for THAT entity, not blanket; a caller holding the ordinary,
        // unscoped form of the claim is completely unaffected.
        if (!RequiredClaimEvaluator.HasClaimForEntity(user, requiredClaim, entityId))
            throw new GraphQLException("Forbidden -- caller lacks the required claim to reveal this field for this entity.");

        if (maskingConfig["requiredSignature"] is JsonObject requiredSignatureConfig)
        {
            var requiredSignature = ParseRequiredSignature(requiredSignatureConfig);
            var acr = StepUpEvaluator.ResolveAcr(user);
            if (!StepUpEvaluator.IsSatisfied(user, requiredSignature, acr))
            {
                var acrValuesText = requiredSignature.AcrValues.Count > 0 ? string.Join(' ', requiredSignature.AcrValues) : "(none configured)";
                throw new GraphQLException(
                    $"Step-up authentication required to reveal this field -- acr_values=\"{acrValuesText}\"" +
                    (requiredSignature.MaxAge is { } maxAge ? $", max_age=\"{maxAge}\"." : "."));
            }
        }

        var payloadNode = JsonNode.Parse(storedEvent.Payload);
        var valueNode = NavigatePayload(payloadNode, segments);
        var value = valueNode switch
        {
            null => null,
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v => v.ToJsonString(),
            _ => valueNode.ToJsonString(),
        };

        var (readerActorId, readerTrustBasis, grantRef) = AccessLogReaderContext.Resolve(user);
        await AccessLogAppender.AppendAsync(db, readerActorId, readerTrustBasis, grantRef, "Authoritative", $"{entityId}:{fieldPath}", "reveal", ct);

        return new RevealFieldPayload(value);
    }

    // Mirrors EventTypeDefinition.RequiredSignature's own field names
    // (camelCase, matching every other x-masking key's own JSON casing --
    // "acrValues"/"maxAge" rather than PascalCase) so a schema author who
    // already knows the publish-time shape needs no separate vocabulary.
    private static RequiredSignature ParseRequiredSignature(JsonObject config) => new()
    {
        AcrValues = config["acrValues"] is JsonArray acrValues
            ? acrValues.Select(v => v!.GetValue<string>()).ToList()
            : [],
        MaxAge = config["maxAge"]?.GetValue<int>(),
    };

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
