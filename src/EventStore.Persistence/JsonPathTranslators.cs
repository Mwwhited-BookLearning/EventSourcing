using System.Linq.Expressions;
using EventStore.Domain.SchemaRegistry;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EventStore.Persistence;

// Three implementations live centrally here per docs/06-solution-structure.md
// ("IJsonPathTranslator's three implementation classes still live centrally in
// EventStore.Persistence"); only the DI registration choosing one varies per
// EventStore.Host.<Provider>. Real query-translation logic, per
// docs/04-odata-filter-pushdown.md's "Per-provider translation" table --
// "Follow API + Filter Pushdown"'s own item, item 1's placeholder finally
// replaced with real SqlExpression construction.
//
// Built via SqlFunctionExpression/SqlUnaryExpression's own public constructors
// and hand-constructed RelationalTypeMapping instances directly, deliberately
// NOT via ISqlExpressionFactory -- that service lives in each provider's
// internal (EF-private) service provider, not the application's own DI
// container, so it can't be constructor-injected into a plain
// AddScoped<IJsonPathTranslator, ...> registration without a
// circular-construction problem (EventStoreContext itself now requires
// IJsonPathTranslator to build). The SqlExpression subclasses used below are
// all public types with public constructors precisely for this kind of
// direct, factory-free construction.

public sealed class SqliteJsonPathTranslator : IJsonPathTranslator
{
    public SqlExpression Translate(SqlExpression payloadColumn, string jsonPath, FilterableFieldType type)
    {
        var extract = new SqlFunctionExpression(
            "json_extract",
            [payloadColumn, new SqlConstantExpression(jsonPath, new StringTypeMapping("TEXT", null))],
            nullable: true,
            argumentsPropagateNullability: [true, false],
            typeof(string),
            new StringTypeMapping("TEXT", null));

        return type switch
        {
            FilterableFieldType.String => extract,
            FilterableFieldType.Boolean => new SqlUnaryExpression(ExpressionType.Convert, extract, typeof(bool), new BoolTypeMapping("INTEGER")),
            FilterableFieldType.Number => new SqlUnaryExpression(ExpressionType.Convert, extract, typeof(double), new DoubleTypeMapping("REAL")),
            // Lexicographic text comparison -- SQLite has no native datetime type (04-odata-filter-pushdown.md)
            FilterableFieldType.DateTimeOffset => new SqlUnaryExpression(ExpressionType.Convert, extract, typeof(DateTimeOffset), new DateTimeOffsetTypeMapping("TEXT")),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }
}

public sealed class PostgresJsonPathTranslator : IJsonPathTranslator
{
    public SqlExpression Translate(SqlExpression payloadColumn, string jsonPath, FilterableFieldType type)
    {
        // jsonb_extract_path_text(jsonb, VARIADIC text[]) accepts each path segment
        // as its own scalar argument at the call site -- no array literal needed --
        // so a multi-level path ("$.Order.Id") translates to two segment arguments,
        // not a single '{Order,Id}' literal the way the index-DDL generator needs.
        var castToJsonb = new SqlUnaryExpression(ExpressionType.Convert, payloadColumn, typeof(string), new StringTypeMapping("jsonb", null));
        var segmentArgs = JsonPathValidation.Segments(jsonPath)
            .Select(segment => (SqlExpression)new SqlConstantExpression(segment, new StringTypeMapping("text", null)))
            .ToArray();

        var extract = new SqlFunctionExpression(
            "jsonb_extract_path_text",
            [castToJsonb, .. segmentArgs],
            nullable: true,
            argumentsPropagateNullability: Enumerable.Repeat(true, segmentArgs.Length + 1).ToArray(),
            typeof(string),
            new StringTypeMapping("text", null));

        return type switch
        {
            FilterableFieldType.String => extract,
            FilterableFieldType.Boolean => new SqlUnaryExpression(ExpressionType.Convert, extract, typeof(bool), new BoolTypeMapping("boolean")),
            FilterableFieldType.Number => new SqlUnaryExpression(ExpressionType.Convert, extract, typeof(double), new DoubleTypeMapping("double precision")),
            FilterableFieldType.DateTimeOffset => new SqlUnaryExpression(ExpressionType.Convert, extract, typeof(DateTimeOffset), new DateTimeOffsetTypeMapping("timestamptz")),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }
}

public sealed class SqlServerJsonPathTranslator : IJsonPathTranslator
{
    public SqlExpression Translate(SqlExpression payloadColumn, string jsonPath, FilterableFieldType type)
    {
        var extract = new SqlFunctionExpression(
            "JSON_VALUE",
            [payloadColumn, new SqlConstantExpression(jsonPath, new StringTypeMapping("nvarchar(max)", null))],
            nullable: true,
            argumentsPropagateNullability: [true, false],
            typeof(string),
            new StringTypeMapping("nvarchar(max)", null));

        return type switch
        {
            FilterableFieldType.String => extract,
            // Kept bool-typed (not the raw string extract) so the SqlExpression.Type
            // agrees with JsonFunctions.JsonValueAsBoolean's own C# return type --
            // returning a string-typed expression here for a bool-declared marker
            // method would desync the two. 04-odata-filter-pushdown.md warns SQL
            // Server's 'true'/'false' text doesn't reliably CAST to BIT; verified
            // empirically instead of assumed -- see the Boolean filter test.
            FilterableFieldType.Boolean => new SqlUnaryExpression(ExpressionType.Convert, extract, typeof(bool), new BoolTypeMapping("bit")),
            // A plain CAST, not TRY_CAST: SqlFunctionExpression models a normal
            // FUNC(args) call, which can't produce TRY_CAST's special CAST(expr AS
            // type) syntax ("Incorrect syntax near 'TRY_CAST', expected 'AS'" --
            // caught by the real SQL Server test run, not assumed). SqlUnaryExpression
            // with ExpressionType.Convert generates CAST(expr AS type) directly,
            // matching the other two providers' own plain-CAST approach.
            FilterableFieldType.Number => new SqlUnaryExpression(ExpressionType.Convert, extract, typeof(double), new DoubleTypeMapping("decimal(18,2)")),
            FilterableFieldType.DateTimeOffset => new SqlUnaryExpression(ExpressionType.Convert, extract, typeof(DateTimeOffset), new DateTimeOffsetTypeMapping("datetimeoffset")),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }
}
