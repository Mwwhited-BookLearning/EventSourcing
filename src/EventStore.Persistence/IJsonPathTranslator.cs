using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using EventStore.Domain.SchemaRegistry;

namespace EventStore.Persistence;

// Real shape, verified against docs/04-odata-filter-pushdown.md ("Per-provider
// translation -- unchanged from the OData era") -- corrected this pass; the
// placeholder shape "Scaffolding & Persistence" originally stubbed here
// (`string TranslateToProviderExpression(string)`) didn't match. Registered
// unconditionally per provider now (that part of the original placeholder's
// reasoning was right), but the actual EF `HasDbFunction`/`HasTranslation`
// wiring and each provider's `Translate` body are real query-pushdown logic
// that belongs to "Follow API + Filter Pushdown", not this item -- still
// deliberately unimplemented here.
public interface IJsonPathTranslator
{
    SqlExpression Translate(SqlExpression payloadColumn, string jsonPath, FilterableFieldType type);
}
