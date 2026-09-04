using System.Text.Json.Nodes;
using EventStore.Persistence;

namespace EventStore.SchemaRegistry;

// Structural x-masking validation, per docs/08-build-plan.md's "Schema
// Registry" scope and ADR-009: pure data validation on the registration
// payload, no claims involved -- doesn't wait for "Event-Type Security" or
// "Property-Level Masking". Walks the raw parsed schema tree directly
// (System.Text.Json.Nodes) rather than through JsonSchema.Net's keyword
// model, matching how MaskingSchemaTransformer/IPayloadMasker are described
// elsewhere in this design (06-solution-structure.md) as a plain recursive
// tree-walk over "type"/"properties"/"items", not a JSON-Schema-library
// keyword extension.
internal static class MaskingSchemaValidator
{
    private static readonly string[] ValidStrategies = ["FixedValue", "PartialReveal", "Hash"];
    private static readonly string[] ValidIndexKinds = ["Equality", "Range"];
    private static readonly string[] ValidKeyScopes = ["Shared", "PerEntity"];

    public static void Validate(JsonObject? node, List<string> errors)
    {
        if (node is null) return;

        var type = node["type"]?.GetValue<string>();

        JsonObject? masking = null;
        if (node.TryGetPropertyValue("x-masking", out var maskingNode))
        {
            if (type is "object" or "array")
                errors.Add($"x-masking cannot be placed directly on an {type}-typed property");
            else if (maskingNode is JsonObject maskingObject)
            {
                masking = maskingObject;
                ValidateMaskingConfig(maskingObject, errors);
            }
            else
                errors.Add("x-masking must be an object");
        }

        // ADR-096/ADR-097 -- a sibling extension on the same node, checked
        // against the SAME x-masking object above (masking may be null if
        // this field isn't also classified, which is fine -- a searchable
        // field need not be encrypted, though every real schema in this
        // design pairs the two).
        if (node.TryGetPropertyValue("x-masking-searchable", out var searchableNode))
        {
            if (type is "object" or "array")
                errors.Add($"x-masking-searchable cannot be placed directly on an {type}-typed property");
            else if (searchableNode is JsonObject searchableObject)
                ValidateSearchableConfig(searchableObject, masking, errors);
            else
                errors.Add("x-masking-searchable must be an object");
        }

        if (node["properties"] is JsonObject properties)
            foreach (var (_, propertySchema) in properties)
                Validate(propertySchema as JsonObject, errors);

        if (node["items"] is JsonObject items)
            Validate(items, errors);
    }

    private static void ValidateSearchableConfig(JsonObject searchable, JsonObject? masking, List<string> errors)
    {
        var indexKind = searchable["indexKind"]?.GetValue<string>();
        if (indexKind is null || !ValidIndexKinds.Contains(indexKind))
        {
            errors.Add($"x-masking-searchable.indexKind must be one of {string.Join(", ", ValidIndexKinds)} (got: {indexKind ?? "<missing>"})");
            return;
        }

        var keyScope = searchable["keyScope"]?.GetValue<string>();
        if (keyScope is null || !ValidKeyScopes.Contains(keyScope))
            errors.Add($"x-masking-searchable.keyScope must be one of {string.Join(", ", ValidKeyScopes)} (got: {keyScope ?? "<missing>"})");

        // ADR-096 -- cardinality-aware, not a blanket classification rule,
        // and required for BOTH Equality and Range (corrected this pass --
        // originally Range-only, found gapped while reviewing the two
        // proving-ground domains' own real candidate fields: a
        // low-cardinality sanctions-list-entry ID wanted as Equality had no
        // guardrail at all). A blind Equality index is deterministic
        // encryption in exactly the shape Naveed/Kamara/Wright (CCS 2015)
        // names as frequency-analysis-vulnerable -- that attack doesn't
        // need ORDER information (Range's own extra exposure), only that
        // the SAME plaintext always produces the SAME ciphertext, which an
        // Equality blind index is by definition. A blanket rule would
        // over-restrict a high-cardinality field (a name) while
        // under-warning on a low-cardinality one (a birthdate) -- the
        // cardinality split is what avoids that, for either index kind.
        if (indexKind is "Equality" or "Range")
        {
            var cardinality = searchable["cardinality"]?.GetValue<string>();
            if (cardinality is not ("Low" or "High"))
            {
                errors.Add($"x-masking-searchable.cardinality is required for {indexKind} indexKind, and must be \"Low\" or \"High\" (got: {cardinality ?? "<missing>"})");
            }
            else if (cardinality == "Low" && masking?["regulatoryClassification"] is not null &&
                     searchable["acknowledgeLeakageRisk"]?.GetValue<bool>() != true)
            {
                errors.Add($"x-masking-searchable: a Low-cardinality {indexKind} index on a field that also " +
                    "carries x-masking.regulatoryClassification requires acknowledgeLeakageRisk: true -- " +
                    "see ADR-096 (frequency analysis against public auxiliary distributions can recover a " +
                    "low-cardinality classified value's exact plaintext, whether the index is Equality or Range)");
            }

            if (indexKind == "Range" && searchable["bucketGranularities"] is not JsonArray { Count: > 0 })
                errors.Add("x-masking-searchable.bucketGranularities must be a non-empty array for Range indexKind");
        }
    }

    private static void ValidateMaskingConfig(JsonObject masking, List<string> errors)
    {
        // Required whenever x-masking is present -- IPayloadMasker (later item)
        // has nothing to resolve without it; no ADR states a default. A narrower
        // reading than requiredClaim below, which the build-plan only asks to
        // format-validate, not require -- see the two's differently phrased
        // scope text.
        var strategy = masking["strategy"]?.GetValue<string>();
        if (strategy is null || !ValidStrategies.Contains(strategy))
            errors.Add($"x-masking.strategy must be one of {string.Join(", ", ValidStrategies)} (got: {strategy ?? "<missing>"})");

        if (masking["requiredClaim"]?.GetValue<string>() is { } requiredClaim && !IsTypeValueFormat(requiredClaim))
            errors.Add($"x-masking.requiredClaim must be in \"type:value\" format (got: {requiredClaim})");

        foreach (var field in new[] { "regulatoryClassification", "governanceBody", "regulationReference" })
        {
            if (masking.TryGetPropertyValue(field, out var value) && value is not null &&
                string.IsNullOrWhiteSpace(value.GetValue<string>()))
                errors.Add($"x-masking.{field} must be a non-empty string if present");
        }

        // ADR-071 -- the one regulatoryClassification value that hard-rejects
        // registration outright, rather than being recorded as inert metadata
        // like every other value (including the ordinary "PCI" full-PAN
        // classification, unaffected). PCI-DSS Requirement 3.2/3.2.2 requires
        // Sensitive Authentication Data (CVV2/CVC2/CID, full track data, PIN
        // blocks) never be persisted at all, even encrypted -- masking and
        // ADR-057 crypto-shredding both still write the real value into
        // Payload first, which this design's append-only architecture cannot
        // avoid; the only compliant answer is refusing to register a schema
        // that declares this value in the first place.
        if (masking["regulatoryClassification"]?.GetValue<string>() == "PCI-SAD")
            errors.Add("x-masking.regulatoryClassification \"PCI-SAD\" can never be registered -- " +
                "PCI-DSS Sensitive Authentication Data must never be persisted, under any circumstances, including encrypted");

        // ADR-057 -- optional; when present, resolves to the EntityId owning
        // THIS field's DEK when it differs from the event's own default
        // EntityId (the cross-entity classified-data case). Same safe-subset
        // grammar EntityIdField already validates against, for the same
        // injection-surface reason -- both are walked by
        // ErasureScopeResolver/EntityIdResolver's identical restricted walker.
        if (masking["erasureScope"]?.GetValue<string>() is { } erasureScope && !JsonPathValidation.IsSafe(erasureScope))
            errors.Add($"x-masking.erasureScope must be a safe JSON-path-like pointer (got: {erasureScope})");

        // ADR-009 -- "reveal-on-demand display masking," an opt-in, per-
        // field object shaped like PartialRevealMaskingStrategy's own
        // config (showFirst/showLast/maskChar/preserveSeparators),
        // independent of whatever `strategy` a non-claim-holder sees.
        // Structural check only, matching this validator's own existing
        // depth for `strategy`'s shape (not deeply typed either).
        if (masking.TryGetPropertyValue("revealOnDemand", out var revealOnDemandNode) && revealOnDemandNode is not null and not JsonObject)
            errors.Add("x-masking.revealOnDemand must be an object if present");

        // ADR-066's built-later refinement -- revealField's own step-up
        // gate, same shape as EventTypeDefinition.RequiredSignature
        // ({ acrValues: [...], maxAge: ... }). Structural check only,
        // matching revealOnDemand's own shallow depth just above.
        if (masking.TryGetPropertyValue("requiredSignature", out var requiredSignatureNode) && requiredSignatureNode is not null and not JsonObject)
            errors.Add("x-masking.requiredSignature must be an object if present");
    }

    private static bool IsTypeValueFormat(string claim)
    {
        var parts = claim.Split(':', 2);
        return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0;
    }
}
