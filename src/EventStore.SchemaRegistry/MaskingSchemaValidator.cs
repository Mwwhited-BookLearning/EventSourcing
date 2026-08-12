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

    public static void Validate(JsonObject? node, List<string> errors)
    {
        if (node is null) return;

        var type = node["type"]?.GetValue<string>();

        if (node.TryGetPropertyValue("x-masking", out var maskingNode))
        {
            if (type is "object" or "array")
                errors.Add($"x-masking cannot be placed directly on an {type}-typed property");
            else if (maskingNode is JsonObject masking)
                ValidateMaskingConfig(masking, errors);
            else
                errors.Add("x-masking must be an object");
        }

        if (node["properties"] is JsonObject properties)
            foreach (var (_, propertySchema) in properties)
                Validate(propertySchema as JsonObject, errors);

        if (node["items"] is JsonObject items)
            Validate(items, errors);
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
    }

    private static bool IsTypeValueFormat(string claim)
    {
        var parts = claim.Split(':', 2);
        return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0;
    }
}
