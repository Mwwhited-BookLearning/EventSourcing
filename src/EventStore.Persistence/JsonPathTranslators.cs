namespace EventStore.Persistence;

// Three implementations live centrally here per docs/06-solution-structure.md
// ("IJsonPathTranslator's three implementation classes still live centrally in
// EventStore.Persistence"); only the DI registration choosing one varies per
// EventStore.Host.<Provider>. Real logic lands with "Follow API + Filter Pushdown".

public sealed class SqliteJsonPathTranslator : IJsonPathTranslator
{
    public string TranslateToProviderExpression(string jsonPath) =>
        throw new NotImplementedException("Implemented by the 'Follow API + Filter Pushdown' build-plan item.");
}

public sealed class PostgresJsonPathTranslator : IJsonPathTranslator
{
    public string TranslateToProviderExpression(string jsonPath) =>
        throw new NotImplementedException("Implemented by the 'Follow API + Filter Pushdown' build-plan item.");
}

public sealed class SqlServerJsonPathTranslator : IJsonPathTranslator
{
    public string TranslateToProviderExpression(string jsonPath) =>
        throw new NotImplementedException("Implemented by the 'Follow API + Filter Pushdown' build-plan item.");
}
