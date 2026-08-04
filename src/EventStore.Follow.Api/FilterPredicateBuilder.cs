using System.Linq.Expressions;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace EventStore.Follow.Api;

// ADR-003 (adopted, not hand-rolled): an incoming $filter string is parsed via
// the real Microsoft.OData.UriParser against a dynamically-built IEdmModel
// containing only this event type's declared FilterableFields -- referencing
// an undeclared field makes ParseFilter itself throw (an unresolvable
// property), giving the "rejected 400 at parse time, before touching the
// database" behavior for free, per 04-odata-filter-pushdown.md's preserved
// Historical section. The resulting FilterClause AST is walked into a real
// LINQ Expression<Func<StoredEvent,bool>>, with each property reference
// compiled to the matching JsonFunctions marker method (EF's DbFunction
// translation then hands it to the active IJsonPathTranslator).
public static class FilterPredicateBuilder
{
    public static Expression<Func<StoredEvent, bool>> Build(string eventTypeName, IReadOnlyList<FilterableField> fields, string? filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
            return _ => true;

        var fieldsByPropertyName = fields.ToDictionary(PropertyNameFor);
        var model = BuildEdmModel(eventTypeName, fields);

        FilterClause filterClause;
        try
        {
            var uri = new Uri($"http://localhost/{Uri.EscapeDataString(eventTypeName)}?$filter={Uri.EscapeDataString(filterText)}");
            var parser = new ODataUriParser(model, new Uri("http://localhost/"), uri);
            filterClause = parser.ParseFilter();
        }
        catch (ODataException ex)
        {
            throw new FilterPushdownException(ex.Message, ex);
        }

        var eventParam = Expression.Parameter(typeof(StoredEvent), "e");
        var body = BuildPredicateBody(filterClause.Expression, eventParam, fieldsByPropertyName);
        return Expression.Lambda<Func<StoredEvent, bool>>(body, eventParam);
    }

    // The registered JsonPath's last segment is this field's OData property name --
    // every real example in this design is single-level ("$.Amount"); a multi-level
    // path flattens to its last segment for filtering purposes, a deliberate
    // simplification (OData doesn't naturally address a nested path as one
    // property without modeling complex types this design has no need for yet).
    private static string PropertyNameFor(FilterableField field) => JsonPathValidation.Segments(field.JsonPath)[^1];

    private static IEdmModel BuildEdmModel(string eventTypeName, IReadOnlyList<FilterableField> fields)
    {
        var model = new EdmModel();
        var entityType = new EdmEntityType("EventStore", eventTypeName);
        foreach (var field in fields)
        {
            var edmKind = field.DataType switch
            {
                FilterableFieldType.String => EdmPrimitiveTypeKind.String,
                FilterableFieldType.Number => EdmPrimitiveTypeKind.Double,
                FilterableFieldType.Boolean => EdmPrimitiveTypeKind.Boolean,
                FilterableFieldType.DateTimeOffset => EdmPrimitiveTypeKind.DateTimeOffset,
                _ => throw new ArgumentOutOfRangeException(nameof(fields)),
            };
            entityType.AddStructuralProperty(PropertyNameFor(field), edmKind);
        }
        model.AddElement(entityType);

        var container = new EdmEntityContainer("EventStore", "Container");
        container.AddEntitySet(eventTypeName, entityType);
        model.AddElement(container);

        return model;
    }

    private static Expression BuildPredicateBody(SingleValueNode node, ParameterExpression eventParam, IReadOnlyDictionary<string, FilterableField> fieldsByPropertyName)
    {
        if (node is ConvertNode convertNode)
            return BuildPredicateBody(convertNode.Source, eventParam, fieldsByPropertyName);

        if (node is not BinaryOperatorNode binary)
            throw new NotSupportedException($"Unsupported $filter expression: {node.GetType().Name}");

        if (binary.OperatorKind is BinaryOperatorKind.And or BinaryOperatorKind.Or)
        {
            var left = BuildPredicateBody(binary.Left, eventParam, fieldsByPropertyName);
            var right = BuildPredicateBody(binary.Right, eventParam, fieldsByPropertyName);
            return binary.OperatorKind == BinaryOperatorKind.And ? Expression.AndAlso(left, right) : Expression.OrElse(left, right);
        }

        var (propertySide, constantSide, reversed) = IdentifySides(binary.Left, binary.Right);
        var propertyAccessNode = (SingleValuePropertyAccessNode)Unwrap(propertySide);
        var field = fieldsByPropertyName[propertyAccessNode.Property.Name];

        var propertyExpr = BuildPropertyAccessExpression(field, eventParam);
        var constantExpr = BuildConstantExpression(((ConstantNode)Unwrap(constantSide)).Value, field.DataType);
        var (leftExpr, rightExpr) = reversed ? (constantExpr, propertyExpr) : (propertyExpr, constantExpr);

        return binary.OperatorKind switch
        {
            BinaryOperatorKind.Equal => Expression.Equal(leftExpr, rightExpr),
            BinaryOperatorKind.NotEqual => Expression.NotEqual(leftExpr, rightExpr),
            BinaryOperatorKind.GreaterThan => Expression.GreaterThan(leftExpr, rightExpr),
            BinaryOperatorKind.GreaterThanOrEqual => Expression.GreaterThanOrEqual(leftExpr, rightExpr),
            BinaryOperatorKind.LessThan => Expression.LessThan(leftExpr, rightExpr),
            BinaryOperatorKind.LessThanOrEqual => Expression.LessThanOrEqual(leftExpr, rightExpr),
            _ => throw new NotSupportedException($"Unsupported $filter operator: {binary.OperatorKind}"),
        };
    }

    private static SingleValueNode Unwrap(SingleValueNode node) => node is ConvertNode convert ? Unwrap(convert.Source) : node;

    private static (SingleValueNode propertySide, SingleValueNode constantSide, bool reversed) IdentifySides(SingleValueNode left, SingleValueNode right)
    {
        if (Unwrap(left) is SingleValuePropertyAccessNode) return (left, right, false);
        if (Unwrap(right) is SingleValuePropertyAccessNode) return (right, left, true);
        throw new NotSupportedException("A $filter comparison must reference exactly one declared FilterableField.");
    }

    // public -- reused verbatim by EventStore.GraphQL's own GraphQlFilterPredicateBuilder
    // (ADR-037's GraphQL-native filter translator, a different front end driving the
    // SAME per-provider JSON pushdown mechanism this class already established for
    // the OData era; "04-odata-filter-pushdown.md" -- "only what drives it changed").
    public static Expression BuildPropertyAccessExpression(FilterableField field, ParameterExpression eventParam)
    {
        var method = typeof(JsonFunctions).GetMethod(JsonFunctions.MethodNameFor(field.DataType))!;
        var payloadProperty = Expression.Property(eventParam, nameof(StoredEvent.Payload));
        return Expression.Call(method, payloadProperty, Expression.Constant(field.JsonPath));
    }

    public static Expression BuildConstantExpression(object? value, FilterableFieldType targetType)
    {
        (object converted, Type clrType) = targetType switch
        {
            FilterableFieldType.String => ((object)(Convert.ToString(value) ?? ""), typeof(string)),
            FilterableFieldType.Number => ((object)Convert.ToDouble(value), typeof(double)),
            FilterableFieldType.Boolean => ((object)Convert.ToBoolean(value), typeof(bool)),
            FilterableFieldType.DateTimeOffset => ((object)ToDateTimeOffset(value), typeof(DateTimeOffset)),
            _ => throw new ArgumentOutOfRangeException(nameof(targetType)),
        };
        return Expression.Constant(converted, clrType);
    }

    private static DateTimeOffset ToDateTimeOffset(object? value) => value switch
    {
        // The EDM property is declared DateTimeOffset (see BuildEdmModel), so the parser
        // already hands back a DateTimeOffset/DateTime for any valid literal -- no Edm.Date
        // special case is reachable here.
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(dt),
        _ => DateTimeOffset.Parse(Convert.ToString(value)!),
    };
}

public sealed class FilterPushdownException(string message, Exception inner) : Exception(message, inner);
