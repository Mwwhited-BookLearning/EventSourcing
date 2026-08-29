using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EventStore.Domain.SchemaRegistry;

namespace EventStore.Erasure;

// ADR-096/ADR-097 -- the publish-time half of searchable encryption. Walks
// the payload against its declared schema (the same shape PayloadEncryptor's
// own walk uses -- no shared walker exists to extend, per this codebase's
// established "each x-masking consumer walks independently" pattern), and
// for every leaf carrying x-masking-searchable, computes the token(s) this
// event's own EncryptedFieldIndexEntry rows need. Runs alongside
// PayloadEncryptor, not instead of it -- a searchable field is still
// encrypted at rest by PayloadEncryptor exactly as before; this class only
// adds the secondary, queryable token(s).
//
// FilterableFieldType for bucketing/token routing comes from the matching
// FilterableField (by JsonPath) in the same registration, not re-declared
// here -- a x-masking-searchable node with no matching declared
// FilterableField is silently not indexed (nothing could ever query it
// anyway, since GraphQlFilterPredicateBuilder only ever looks up declared
// FilterableFields).
public class PayloadIndexer(SearchIndexKeyService searchIndexKeyService, ErasureKeyService erasureKeyService)
{
    // Fixed info string for the PerEntity key-derivation trick (ADR-096's
    // Implementation note): IErasureKeyStore never exposes raw DEK bytes, so
    // the searchable-index key for PerEntity scope is instead
    // SHA-256(EncryptAsync(keyReference, this fixed byte string)) --
    // deterministic, entity-key-dependent, and permanently uncomputable the
    // instant the owning entity's DEK is destroyed (EncryptAsync under a
    // destroyed keyReference fails), which is exactly the "destroyed
    // alongside the DEK" property PerEntity scope requires.
    private static readonly byte[] PerEntityDerivationInfo = "searchable-index-v1"u8.ToArray();

    public async Task<List<EncryptedFieldIndexEntry>> ComputeIndexEntriesAsync(
        JsonNode? schema, JsonNode? payload, string appId, string eventTypeName, string? entityId,
        long sequenceNumber, IReadOnlyList<FilterableField> filterableFields, CancellationToken ct)
    {
        var entries = new List<EncryptedFieldIndexEntry>();
        if (entityId is null || schema is not JsonObject schemaObject || payload is null)
            return entries;

        await WalkAsync(schemaObject, payload, payload, "$", appId, eventTypeName, entityId, sequenceNumber, filterableFields, entries, ct);
        return entries;
    }

    private async Task WalkAsync(
        JsonObject? schemaNode, JsonNode? payload, JsonNode? rootPayload, string currentPath,
        string appId, string eventTypeName, string defaultEntityId, long sequenceNumber,
        IReadOnlyList<FilterableField> filterableFields, List<EncryptedFieldIndexEntry> entries, CancellationToken ct)
    {
        if (schemaNode is null || payload is null)
            return;

        if (schemaNode.TryGetPropertyValue("x-masking-searchable", out var searchableNode) && searchableNode is JsonObject searchableConfig)
        {
            var field = filterableFields.FirstOrDefault(f => f.JsonPath == currentPath);
            if (field is not null)
            {
                var maskingConfig = schemaNode.TryGetPropertyValue("x-masking", out var m) && m is JsonObject mo ? mo : new JsonObject();
                await IndexLeafAsync(payload, searchableConfig, maskingConfig, field, rootPayload, appId, eventTypeName, defaultEntityId, sequenceNumber, entries, ct);
            }
            return; // a searchable leaf is always scalar, same constraint x-masking itself enforces -- nothing further to recurse into
        }

        if (schemaNode["properties"] is JsonObject properties && payload is JsonObject payloadObject)
        {
            foreach (var (name, value) in payloadObject)
            {
                if (properties.TryGetPropertyValue(name, out var propertySchema) && propertySchema is JsonObject propertySchemaObject)
                    await WalkAsync(propertySchemaObject, value, rootPayload, $"{currentPath}.{name}", appId, eventTypeName, defaultEntityId, sequenceNumber, filterableFields, entries, ct);
            }
        }

        // Arrays are deliberately not path-tracked -- FilterableField.JsonPath
        // never resolves through "items" in this design (SchemaRegistryService.
        // ResolvesInSchema only walks "properties"), so a x-masking-searchable
        // node under an array's items can never have a matching FilterableField
        // to index against anyway.
    }

    private async Task IndexLeafAsync(
        JsonNode realValue, JsonObject searchableConfig, JsonObject maskingConfig, FilterableField field, JsonNode? rootPayload,
        string appId, string eventTypeName, string defaultEntityId, long sequenceNumber, List<EncryptedFieldIndexEntry> entries, CancellationToken ct)
    {
        var indexKindText = searchableConfig["indexKind"]?.GetValue<string>();
        if (!Enum.TryParse<SearchableIndexKind>(indexKindText, out var indexKind))
            return; // malformed config -- registration-time MaskingSchemaValidator is what refuses this; publish-time just skips indexing it defensively

        var entityId = ErasureScopeResolver.Resolve(rootPayload, maskingConfig, defaultEntityId);
        var keyScopeText = searchableConfig["keyScope"]?.GetValue<string>();
        Enum.TryParse<SearchIndexKeyScope>(keyScopeText, out var keyScope);

        var rawValueText = realValue.ToJsonString().Trim('"'); // ToJsonString on a JsonValue<string> yields a quoted literal; numbers/bools are unquoted already, Trim is a no-op for them

        switch (indexKind)
        {
            case SearchableIndexKind.Equality:
            {
                var token = await ComputeTokenAsync(appId, eventTypeName, field.JsonPath, entityId, keyScope, Encoding.UTF8.GetBytes(rawValueText), ct);
                entries.Add(new EncryptedFieldIndexEntry
                {
                    AppId = appId, EntityId = entityId, EventTypeName = eventTypeName, FieldJsonPath = field.JsonPath,
                    IndexKind = SearchableIndexKind.Equality, Granularity = null, Token = token, StoredEventSequenceNumber = sequenceNumber,
                });
                break;
            }
            case SearchableIndexKind.Range:
            {
                var granularities = searchableConfig["bucketGranularities"] is JsonArray arr
                    ? arr.Select(x => x!.GetValue<string>()).ToList()
                    : [];
                foreach (var granularity in granularities)
                {
                    var bucketLabel = RangeBucketing.ComputeBucketLabel(rawValueText, field.DataType, granularity);
                    var token = await ComputeTokenAsync(appId, eventTypeName, field.JsonPath, entityId, keyScope, Encoding.UTF8.GetBytes(bucketLabel), ct);
                    entries.Add(new EncryptedFieldIndexEntry
                    {
                        AppId = appId, EntityId = entityId, EventTypeName = eventTypeName, FieldJsonPath = field.JsonPath,
                        IndexKind = SearchableIndexKind.Range, Granularity = granularity, Token = token, StoredEventSequenceNumber = sequenceNumber,
                    });
                }
                break;
            }
            case SearchableIndexKind.OrderRevealing:
            {
                // ADR-097 -- computed by OrderRevealingEncryption, not the HMAC
                // path above; the ciphertext itself is the indexed, comparable
                // value, so no separate token derivation is needed here.
                var ciphertext = OrderRevealingEncryption.Encrypt(await ResolveOreKeyAsync(appId, eventTypeName, field.JsonPath, entityId, keyScope, ct), rawValueText, field.DataType);
                entries.Add(new EncryptedFieldIndexEntry
                {
                    AppId = appId, EntityId = entityId, EventTypeName = eventTypeName, FieldJsonPath = field.JsonPath,
                    IndexKind = SearchableIndexKind.OrderRevealing, Granularity = null, Token = Convert.ToBase64String(ciphertext), StoredEventSequenceNumber = sequenceNumber,
                });
                break;
            }
        }
    }

    private async Task<string> ComputeTokenAsync(
        string appId, string eventTypeName, string fieldJsonPath, string entityId, SearchIndexKeyScope keyScope, byte[] data, CancellationToken ct)
    {
        var mac = keyScope == SearchIndexKeyScope.PerEntity
            ? await ComputePerEntityHmacAsync(appId, entityId, data, ct)
            : await ComputeSharedHmacAsync(appId, eventTypeName, fieldJsonPath, data, ct);
        return Convert.ToBase64String(mac);
    }

    private async Task<byte[]> ComputeSharedHmacAsync(string appId, string eventTypeName, string fieldJsonPath, byte[] data, CancellationToken ct)
    {
        var (keyReference, backend) = await searchIndexKeyService.GetOrCreateAsync(appId, eventTypeName, fieldJsonPath, ct);
        return await backend.ComputeHmacAsync(keyReference, data, ct);
    }

    private async Task<byte[]> ComputePerEntityHmacAsync(string appId, string entityId, byte[] data, CancellationToken ct)
    {
        var derivedKey = await DerivePerEntityKeyAsync(appId, entityId, ct);
        return HMACSHA256.HashData(derivedKey, data);
    }

    private async Task<byte[]> DerivePerEntityKeyAsync(string appId, string entityId, CancellationToken ct)
    {
        var (keyReference, backend) = await erasureKeyService.GetOrCreateAsync(appId, entityId, ct);
        var derivationCiphertext = await backend.EncryptAsync(keyReference, PerEntityDerivationInfo, ct);
        return SHA256.HashData(derivationCiphertext);
    }

    private async Task<byte[]> ResolveOreKeyAsync(string appId, string eventTypeName, string fieldJsonPath, string entityId, SearchIndexKeyScope keyScope, CancellationToken ct) =>
        keyScope == SearchIndexKeyScope.PerEntity
            ? await DerivePerEntityKeyAsync(appId, entityId, ct)
            : await ResolveSharedOreKeyAsync(appId, eventTypeName, fieldJsonPath, ct);

    private async Task<byte[]> ResolveSharedOreKeyAsync(string appId, string eventTypeName, string fieldJsonPath, CancellationToken ct)
    {
        // ORE needs the raw key bytes to run its own compare-function
        // construction (unlike the HMAC path, which only ever needs a
        // compute-under-key oracle) -- ISearchIndexKeyStore's ComputeHmacAsync
        // is repurposed here as that oracle: hashing a fixed label under the
        // field's own managed key yields a deterministic, key-dependent seed
        // exactly the same way DerivePerEntityKeyAsync repurposes
        // IErasureKeyStore.EncryptAsync above, without needing a second
        // interface method or ever exposing the store's own raw key material.
        var (keyReference, backend) = await searchIndexKeyService.GetOrCreateAsync(appId, eventTypeName, fieldJsonPath, ct);
        return await backend.ComputeHmacAsync(keyReference, "ore-key-seed-v1"u8.ToArray(), ct);
    }
}
