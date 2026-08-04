using System.Linq.Expressions;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Follow.Api;
using EventStore.Persistence;
using HotChocolate;

namespace EventStore.GraphQL;

// The GraphQL-native counterpart to FilterPredicateBuilder (which parses an
// OData $filter string) -- this parses a list of EventFilterInput clauses
// instead, AND-combining across entries (no and/or combinator nesting, see
// EventFilterInput's own note), but reuses the exact same property-access/
// constant-expression building blocks, so the per-provider JSON pushdown
// mechanism underneath is identical either way (ADR-037's own claim, proven
// by this literal code reuse rather than merely asserted).
public static class GraphQlFilterPredicateBuilder
{
    public static Expression<Func<StoredEvent, bool>> Build(IReadOnlyList<FilterableField> fields, IReadOnlyList<EventFilterInput>? clauses)
    {
        var eventParam = Expression.Parameter(typeof(StoredEvent), "e");
        if (clauses is null || clauses.Count == 0)
            return Expression.Lambda<Func<StoredEvent, bool>>(Expression.Constant(true), eventParam);

        var fieldsByName = fields.ToDictionary(f => PropertyNameFor(f));

        Expression? body = null;
        foreach (var clause in clauses)
        {
            if (!fieldsByName.TryGetValue(clause.Field, out var field))
                throw new GraphQLException($"where: \"{clause.Field}\" is not a declared FilterableField for this event type.");

            var propertyExpr = FilterPredicateBuilder.BuildPropertyAccessExpression(field, eventParam);
            var clauseExpr = BuildClauseExpression(clause, propertyExpr, field.DataType);
            body = body is null ? clauseExpr : Expression.AndAlso(body, clauseExpr);
        }

        return Expression.Lambda<Func<StoredEvent, bool>>(body!, eventParam);
    }

    private static Expression BuildClauseExpression(EventFilterInput clause, Expression propertyExpr, FilterableFieldType dataType)
    {
        Expression? body = null;
        if (clause.Eq is { } eq) body = Combine(body, Expression.Equal(propertyExpr, Constant(eq, dataType)));
        if (clause.Neq is { } neq) body = Combine(body, Expression.NotEqual(propertyExpr, Constant(neq, dataType)));
        if (clause.Gt is { } gt) body = Combine(body, Expression.GreaterThan(propertyExpr, Constant(gt, dataType)));
        if (clause.Gte is { } gte) body = Combine(body, Expression.GreaterThanOrEqual(propertyExpr, Constant(gte, dataType)));
        if (clause.Lt is { } lt) body = Combine(body, Expression.LessThan(propertyExpr, Constant(lt, dataType)));
        if (clause.Lte is { } lte) body = Combine(body, Expression.LessThanOrEqual(propertyExpr, Constant(lte, dataType)));
        if (clause.Contains is { } contains)
        {
            if (dataType != FilterableFieldType.String)
                throw new GraphQLException($"where: \"{clause.Field}\": contains is only valid for String fields.");
            var method = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
            body = Combine(body, Expression.Call(propertyExpr, method, Constant(contains, dataType)));
        }

        return body ?? throw new GraphQLException($"where: \"{clause.Field}\" named no operator (eq/neq/gt/gte/lt/lte/contains).");
    }

    private static Expression Combine(Expression? left, Expression right) => left is null ? right : Expression.AndAlso(left, right);

    private static Expression Constant(string textValue, FilterableFieldType dataType) =>
        FilterPredicateBuilder.BuildConstantExpression(textValue, dataType);

    // Mirrors FilterPredicateBuilder's own PropertyNameFor -- the registered
    // JsonPath's last segment names the filterable field for lookup purposes.
    private static string PropertyNameFor(FilterableField field) => JsonPathValidation.Segments(field.JsonPath)[^1];
}
