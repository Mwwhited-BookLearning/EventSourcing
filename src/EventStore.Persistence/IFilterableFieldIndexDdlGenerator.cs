namespace EventStore.Persistence;

// Per docs/data/schema-registry.md's "Per-provider index strategy" table and
// 04-odata-filter-pushdown.md's "Indexing -- unchanged" section: a
// FilterableField.IsIndexed = true field gets a provider-specific expression
// index or computed column + index, generated once at registration time
// ("Schema Registry"). This is a distinct concern from IJsonPathTranslator's
// own query-time SqlExpression translation ("Follow API + Filter Pushdown")
// -- raw DDL text executed once, not a LINQ-to-SQL expression tree consulted
// per query. `jsonPath` must already have passed `JsonPathValidation.IsSafe`
// before reaching here -- these implementations trust the caller, they don't
// re-validate, since they interpolate it directly into DDL text.
public interface IFilterableFieldIndexDdlGenerator
{
    IReadOnlyList<string> GenerateCreateIndexDdl(string tableName, string payloadColumn, string jsonPath, string indexName);
}
