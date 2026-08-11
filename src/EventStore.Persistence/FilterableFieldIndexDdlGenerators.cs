namespace EventStore.Persistence;

// Three implementations, one per provider (docs/data/schema-registry.md's
// "Per-provider index strategy" table) -- resolved via DI per
// EventStore.Host.<Provider>, the same "no switch on provider name" pattern
// every other per-provider seam in this design uses (ADR-041).

public sealed class SqliteFilterableFieldIndexDdlGenerator : IFilterableFieldIndexDdlGenerator
{
    public IReadOnlyList<string> GenerateCreateIndexDdl(string tableName, string payloadColumn, string jsonPath, string indexName) =>
        [$"""CREATE INDEX "{indexName}" ON "{tableName}" (json_extract({payloadColumn}, '{jsonPath}'))"""];
}

public sealed class PostgresFilterableFieldIndexDdlGenerator : IFilterableFieldIndexDdlGenerator
{
    public IReadOnlyList<string> GenerateCreateIndexDdl(string tableName, string payloadColumn, string jsonPath, string indexName)
    {
        var segments = JsonPathValidation.Segments(jsonPath);
        var pathArray = "{" + string.Join(",", segments) + "}";
        return [$"""CREATE INDEX "{indexName}" ON "{tableName}" ((("{payloadColumn}"::jsonb) #>> '{pathArray}'))"""];
    }
}

public sealed class SqlServerFilterableFieldIndexDdlGenerator : IFilterableFieldIndexDdlGenerator
{
    public IReadOnlyList<string> GenerateCreateIndexDdl(string tableName, string payloadColumn, string jsonPath, string indexName)
    {
        var computedColumn = string.Join("_", JsonPathValidation.Segments(jsonPath));
        return
        [
            $"""ALTER TABLE [{tableName}] ADD [{computedColumn}] AS JSON_VALUE([{payloadColumn}], '{jsonPath}')""",
            $"""CREATE INDEX [{indexName}] ON [{tableName}]([{computedColumn}])""",
        ];
    }
}
