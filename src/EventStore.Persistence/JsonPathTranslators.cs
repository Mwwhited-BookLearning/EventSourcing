using EventStore.Domain.SchemaRegistry;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EventStore.Persistence;

// Three implementations live centrally here per docs/06-solution-structure.md
// ("IJsonPathTranslator's three implementation classes still live centrally in
// EventStore.Persistence"); only the DI registration choosing one varies per
// EventStore.Host.<Provider>. Real query-translation logic (the `Translate`
// body: building a native json_extract/->>/JSON_VALUE SqlExpression) lands
// with "Follow API + Filter Pushdown" -- see docs/04-odata-filter-pushdown.md.

public sealed class SqliteJsonPathTranslator : IJsonPathTranslator
{
    public SqlExpression Translate(SqlExpression payloadColumn, string jsonPath, FilterableFieldType type) =>
        throw new NotImplementedException("Implemented by the 'Follow API + Filter Pushdown' build-plan item.");
}

public sealed class PostgresJsonPathTranslator : IJsonPathTranslator
{
    public SqlExpression Translate(SqlExpression payloadColumn, string jsonPath, FilterableFieldType type) =>
        throw new NotImplementedException("Implemented by the 'Follow API + Filter Pushdown' build-plan item.");
}

public sealed class SqlServerJsonPathTranslator : IJsonPathTranslator
{
    public SqlExpression Translate(SqlExpression payloadColumn, string jsonPath, FilterableFieldType type) =>
        throw new NotImplementedException("Implemented by the 'Follow API + Filter Pushdown' build-plan item.");
}
