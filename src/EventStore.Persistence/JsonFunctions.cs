using EventStore.Domain.SchemaRegistry;

namespace EventStore.Persistence;

// Provider-neutral marker methods (docs/06-solution-structure.md, "Per-provider
// translation") -- never executed directly; EF Core's HasDbFunction/HasTranslation
// wiring (EventStoreContext.OnModelCreating) intercepts each call and substitutes
// the active provider's own json_extract/->>/JSON_VALUE (+ CAST) SqlExpression.
// One marker method per FilterableFieldType, not one generic method, so each C#
// return type matches what a LINQ comparison against that type's constants
// actually needs to type-check -- IJsonPathTranslator.Translate still takes an
// explicit FilterableFieldType parameter (each registration below hardcodes which
// one), matching the verified interface shape from 04-odata-filter-pushdown.md.
public static class JsonFunctions
{
    public static string JsonValueAsString(string payload, string jsonPath) =>
        throw new InvalidOperationException("For LINQ translation only.");

    public static double JsonValueAsNumber(string payload, string jsonPath) =>
        throw new InvalidOperationException("For LINQ translation only.");

    public static bool JsonValueAsBoolean(string payload, string jsonPath) =>
        throw new InvalidOperationException("For LINQ translation only.");

    public static DateTimeOffset JsonValueAsDateTimeOffset(string payload, string jsonPath) =>
        throw new InvalidOperationException("For LINQ translation only.");

    public static string MethodNameFor(FilterableFieldType type) => type switch
    {
        FilterableFieldType.String => nameof(JsonValueAsString),
        FilterableFieldType.Number => nameof(JsonValueAsNumber),
        FilterableFieldType.Boolean => nameof(JsonValueAsBoolean),
        FilterableFieldType.DateTimeOffset => nameof(JsonValueAsDateTimeOffset),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
