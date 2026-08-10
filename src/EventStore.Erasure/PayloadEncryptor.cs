using System.Text;
using System.Text.Json.Nodes;

namespace EventStore.Erasure;

// The publish-time half of ADR-057's crypto-shredding. Walks the payload
// against its declared schema (the same x-masking tree PayloadMasker's own
// read-time walk visits), and for every leaf carrying
// x-masking.regulatoryClassification, replaces its real value with
// ciphertext -- ADR-057's own encryption trigger is regulatoryClassification
// specifically, not x-masking generally: a field can be claims-gated via
// requiredClaim without ever being classified/encrypted, and vice versa in
// principle, even though every real schema in this design sets both
// together.
//
// Architecture note this class exists to reconcile: ADR-057 was decided
// assuming the pre-"Entity-Centric Core Rebuild" synchronous-validation
// model ("encryption happens after SchemaValidationService validates the
// plaintext... and before the payload is written to StoredEvent.Payload").
// That model no longer exists -- PublishService is now always-202 and
// never resolves EntityId itself, leaving that to the async RouterWorker.
// Encryption still has to happen synchronously, at publish time, per
// ADR-057's own ordering requirement (before Payload is persisted and
// hashed) -- so PublishService independently computes EntityId here too,
// via the SAME EntityIdResolver utility RouterWorker uses, rather than
// waiting for the Router's own fold. This is a deliberate, narrow
// duplication of a pure function, not a re-architecture of the Inbox/Router
// split -- StoredEvent.EntityId itself still starts empty and is still
// filled in by the Router, unaffected.
public class PayloadEncryptor(ErasureKeyService erasureKeyService)
{
    public async Task<JsonNode?> EncryptClassifiedFieldsAsync(
        JsonNode? schema, JsonNode? payload, string appId, string? entityId, CancellationToken ct)
    {
        // No resolvable EntityId means nothing to scope a DEK to -- the same
        // condition under which the Router itself simply skips folding this
        // event into any entity at all.
        if (entityId is null)
            return payload?.DeepClone();

        return await EncryptNodeAsync(schema as JsonObject, payload, payload, appId, entityId, ct);
    }

    private async Task<JsonNode?> EncryptNodeAsync(
        JsonObject? schemaNode, JsonNode? payload, JsonNode? rootPayload, string appId, string defaultEntityId, CancellationToken ct)
    {
        if (schemaNode is null || payload is null)
            return payload?.DeepClone();

        if (schemaNode.TryGetPropertyValue("x-masking", out var maskingNode) && maskingNode is JsonObject maskingConfig
            && maskingConfig["regulatoryClassification"] is not null)
        {
            return await EncryptLeafAsync(payload, maskingConfig, rootPayload, appId, defaultEntityId, ct);
        }

        if (schemaNode["properties"] is JsonObject properties && payload is JsonObject payloadObject)
        {
            var result = new JsonObject();
            foreach (var (name, value) in payloadObject)
                result[name] = properties.TryGetPropertyValue(name, out var propertySchema) && propertySchema is JsonObject propertySchemaObject
                    ? await EncryptNodeAsync(propertySchemaObject, value, rootPayload, appId, defaultEntityId, ct)
                    : value?.DeepClone();
            return result;
        }

        if (schemaNode["items"] is JsonObject itemsSchema && payload is JsonArray payloadArray)
        {
            var result = new JsonArray();
            foreach (var element in payloadArray)
                result.Add(await EncryptNodeAsync(itemsSchema, element, rootPayload, appId, defaultEntityId, ct));
            return result;
        }

        return payload.DeepClone();
    }

    private async Task<JsonNode> EncryptLeafAsync(
        JsonNode realValue, JsonObject maskingConfig, JsonNode? rootPayload, string appId, string defaultEntityId, CancellationToken ct)
    {
        var entityId = ErasureScopeResolver.Resolve(rootPayload, maskingConfig, defaultEntityId);
        var (keyReference, backend) = await erasureKeyService.GetOrCreateAsync(appId, entityId, ct);

        // Encrypts the leaf's own canonical JSON text, not just its raw
        // string content -- so decryption can JsonNode.Parse the recovered
        // bytes straight back into the original typed value (number/bool/
        // string alike), not just reconstruct a string.
        var plaintextBytes = Encoding.UTF8.GetBytes(realValue.ToJsonString());
        var ciphertext = await backend.EncryptAsync(keyReference, plaintextBytes, ct);
        return JsonValue.Create(Convert.ToBase64String(ciphertext))!;
    }
}
