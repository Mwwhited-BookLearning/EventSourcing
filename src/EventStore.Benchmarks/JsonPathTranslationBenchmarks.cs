using BenchmarkDotNet.Attributes;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EventStore.Benchmarks;

// ADR-085 -- the third named target, IJsonPathTranslator's per-provider
// filter translation (docs/04-odata-filter-pushdown.md's "Per-provider
// translation" table). Translate() is pure SqlExpression-tree construction
// with no DbContext/live connection involved, so a SqlConstantExpression
// stands in for the real payload-column ColumnExpression a live query would
// supply -- same public constructor JsonPathTranslators.cs itself uses.
[MemoryDiagnoser]
public class JsonPathTranslationBenchmarks
{
    private static readonly SqlExpression PayloadColumn =
        new SqlConstantExpression("""{"WidgetId":"widget-1","Name":"Original"}""", new StringTypeMapping("TEXT", null));

    private readonly IJsonPathTranslator _sqlite = new SqliteJsonPathTranslator();
    private readonly IJsonPathTranslator _postgres = new PostgresJsonPathTranslator();
    private readonly IJsonPathTranslator _sqlServer = new SqlServerJsonPathTranslator();

    [Benchmark(Baseline = true)]
    public SqlExpression SqliteTranslate() => _sqlite.Translate(PayloadColumn, "$.Name", FilterableFieldType.String);

    [Benchmark]
    public SqlExpression PostgresTranslate() => _postgres.Translate(PayloadColumn, "$.Name", FilterableFieldType.String);

    [Benchmark]
    public SqlExpression SqlServerTranslate() => _sqlServer.Translate(PayloadColumn, "$.Name", FilterableFieldType.String);
}
