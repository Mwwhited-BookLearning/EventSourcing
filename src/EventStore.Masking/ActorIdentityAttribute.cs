using Microsoft.Extensions.Compliance.Classification;

namespace EventStore.Masking;

// ADR-050 -- the STATIC half of this ADR's two log-redaction shapes,
// distinct from PayloadMasker's own DYNAMIC one (a Redactor resolved
// programmatically per-field, via the schema's own x-masking.
// regulatoryClassification -- there's no compile-time property to attach
// a static attribute to for schema-driven payload logging). This one is
// for THIS framework's own statically-typed internal log call sites --
// the library's documented pattern applied directly: a real
// DataClassification taxonomy, a real attribute deriving from it,
// applied to a [LoggerMessage] parameter, redacted automatically by
// whatever Redactor is registered for this classification (falling back
// to ErasingRedactor, same as every other unregistered classification in
// this codebase -- MaskingServiceCollectionExtensions.AddMasking's own
// comment already explains why that fallback is safe-by-default).
public static class ActorIdentityTaxonomy
{
    public const string Name = "EventLogRedaction";
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ActorIdentityAttribute() : DataClassificationAttribute(new DataClassification(ActorIdentityTaxonomy.Name, "ActorIdentity"));
