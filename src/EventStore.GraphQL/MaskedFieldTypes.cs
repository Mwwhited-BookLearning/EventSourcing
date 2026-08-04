namespace EventStore.GraphQL;

// docs/03-api-contracts.md, "GraphQL schema shape (masking)": the three-way
// wrapper MaskingSchemaTransformer produces for the OpenAPI/AsyncAPI side --
// { value: T, masked: String, erased: Boolean } -- re-expressed as ordinary,
// statically-registered GraphQL output types (one per underlying scalar,
// since GraphQL has no generic T). Exactly one of Value/Masked is ever
// populated per response (Erased stays null until "GDPR/CCPA Erasure via
// Crypto-Shredding" actually produces that third branch) -- IPayloadMasker's
// existing data-level enforcement, unchanged, just read into whichever of
// these a dynamically-built payload field (FollowSubscriptionTypeModule)
// declares for a given x-masking-annotated property.
public record MaskedString(string? Value, string? Masked, bool? Erased);
public record MaskedFloat(double? Value, string? Masked, bool? Erased);
public record MaskedBoolean(bool? Value, string? Masked, bool? Erased);
public record MaskedDateTimeOffset(DateTimeOffset? Value, string? Masked, bool? Erased);
