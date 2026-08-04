using EventStore.Upcasting;

namespace EventStore.IntegrationTests;

// Shared, stateless test wiring for "Hardening & Evolution" -- every
// provider test class needs the same CelUpcastExpressionEvaluator/
// UpcastChain pair to construct SchemaRegistryService/EventTailReader, the
// same way MaskingTestSupport/DerivationTestSupport-style helpers already
// avoid repeating construction boilerplate per test class.
internal static class UpcastingTestSupport
{
    public static IUpcastExpressionEvaluator CreateEvaluator() => new CelUpcastExpressionEvaluator();

    public static UpcastChain CreateChain() => new(CreateEvaluator());

    public static DowncastChain CreateDowncastChain() => new(CreateEvaluator());
}
