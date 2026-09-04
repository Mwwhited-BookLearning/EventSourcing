using System.Linq.Expressions;
using System.Text;
using EventStore.Abstractions;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Erasure;
using EventStore.Follow.Api;
using EventStore.Persistence;
using HotChocolate;
using Microsoft.EntityFrameworkCore;

namespace EventStore.GraphQL;

// The GraphQL-native counterpart to FilterPredicateBuilder (which parses an
// OData $filter string) -- this parses a list of EventFilterInput clauses
// instead, AND-combining across entries (no and/or combinator nesting, see
// EventFilterInput's own note), but reuses the exact same property-access/
// constant-expression building blocks for a PlaintextExpression field, so
// the per-provider JSON pushdown mechanism underneath is identical either
// way (ADR-037's own claim, proven by this literal code reuse rather than
// merely asserted).
//
// ADR-096/097 -- an encrypted-kind field (FilterableField.IndexKind !=
// PlaintextExpression) skips that pushdown entirely: extracting a
// classified field's ciphertext via json_extract/->>/JSON_VALUE would only
// ever compare opaque bytes. Instead this resolves matching SequenceNumbers
// via EncryptedFieldIndexEntry (an ordinary indexed lookup, never a
// full-table decrypt) and folds the result in as a Contains expression --
// which is why Build is now async, a real, deliberate signature change
// from its pre-ADR-096 synchronous form.
public static class GraphQlFilterPredicateBuilder
{
    public static async Task<Expression<Func<StoredEvent, bool>>> Build(
        EventStoreContext db, string appId, string eventTypeName,
        SearchIndexKeyService searchIndexKeyService, IEncryptedPredicateEvaluator predicateEvaluator,
        IReadOnlyList<FilterableField> fields, IReadOnlyList<EventFilterInput>? clauses, CancellationToken ct)
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

            var clauseExpr = field.IndexKind == FilterableFieldIndexKind.PlaintextExpression
                ? BuildClauseExpression(clause, FilterPredicateBuilder.BuildPropertyAccessExpression(field, eventParam), field.DataType)
                : await BuildEncryptedClauseExpressionAsync(db, appId, eventTypeName, searchIndexKeyService, predicateEvaluator, field, clause, eventParam, ct);
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

    // ADR-096 -- resolves matching SequenceNumbers against
    // EncryptedFieldIndexEntry, then folds the result in as an ordinary
    // Contains expression -- the same materialized-ID-list technique
    // EventTailReader already uses for lineage/parent filtering.
    private static async Task<Expression> BuildEncryptedClauseExpressionAsync(
        EventStoreContext db, string appId, string eventTypeName, SearchIndexKeyService searchIndexKeyService,
        IEncryptedPredicateEvaluator predicateEvaluator, FilterableField field, EventFilterInput clause,
        ParameterExpression eventParam, CancellationToken ct)
    {
        if (clause.Contains is not null)
            throw new GraphQLException($"where: \"{clause.Field}\" is an encrypted field -- contains is never supported against any encrypted IndexKind (ADR-096).");

        // ADR-096's own named limitation: a PerEntity-scope token is derived
        // from one specific entity's own DEK, so there is no key a
        // cross-event, cross-entity `where` filter could compute a
        // comparison token under -- this only ever answers "does this one
        // known entity have value V," which a general filter isn't.
        if (field.SearchableConfig?.KeyScope == SearchIndexKeyScope.PerEntity)
            throw new GraphQLException($"where: \"{clause.Field}\" is a PerEntity-scope encrypted field -- it can only " +
                "answer \"does one already-known entity have this value,\" never a general cross-entity filter. See ADR-096.");

        IReadOnlyList<long> matchedSequenceNumbers = field.IndexKind switch
        {
            FilterableFieldIndexKind.EncryptedBlindIndex => await ResolveBlindIndexMatchesAsync(db, appId, eventTypeName, searchIndexKeyService, field, clause, ct),
            FilterableFieldIndexKind.EncryptedRangeBucket => await ResolveRangeBucketMatchesAsync(db, appId, eventTypeName, searchIndexKeyService, predicateEvaluator, field, clause, ct),
            _ => throw new InvalidOperationException($"Unexpected encrypted IndexKind: {field.IndexKind}"),
        };

        var containsMethod = typeof(List<long>).GetMethod(nameof(List<long>.Contains), [typeof(long)])!;
        var sequenceNumberProperty = Expression.Property(eventParam, nameof(StoredEvent.SequenceNumber));
        return Expression.Call(Expression.Constant(matchedSequenceNumbers.ToList()), containsMethod, sequenceNumberProperty);
    }

    private static async Task<IReadOnlyList<long>> ResolveBlindIndexMatchesAsync(
        EventStoreContext db, string appId, string eventTypeName, SearchIndexKeyService searchIndexKeyService, FilterableField field, EventFilterInput clause, CancellationToken ct)
    {
        var value = clause.Eq;
        if (value is null || clause.Neq is not null || clause.Gt is not null || clause.Gte is not null || clause.Lt is not null || clause.Lte is not null)
            throw new GraphQLException($"where: \"{clause.Field}\" is a blind-indexed encrypted field -- only eq is supported (ADR-096).");

        var resolved = await searchIndexKeyService.ResolveAsync(appId, eventTypeName, field.JsonPath, ct);
        if (resolved is null)
            return [];
        var (keyReference, backend) = resolved.Value;
        var token = Convert.ToBase64String(await backend.ComputeHmacAsync(keyReference, Encoding.UTF8.GetBytes(value), ct));

        return await db.EncryptedFieldIndexEntries
            .Where(e => e.AppId == appId && e.EventTypeName == eventTypeName && e.FieldJsonPath == field.JsonPath && e.Token == token)
            .Select(e => e.StoredEventSequenceNumber)
            .ToListAsync(ct);
    }

    private static async Task<IReadOnlyList<long>> ResolveRangeBucketMatchesAsync(
        EventStoreContext db, string appId, string eventTypeName, SearchIndexKeyService searchIndexKeyService,
        IEncryptedPredicateEvaluator predicateEvaluator, FilterableField field, EventFilterInput clause, CancellationToken ct)
    {
        var lower = clause.Gte ?? clause.Gt;
        var upper = clause.Lte ?? clause.Lt;
        if (lower is null || upper is null)
            throw new GraphQLException($"where: \"{clause.Field}\" is a bucketed-range encrypted field -- a query must " +
                "supply BOTH a lower bound (gte/gt) and an upper bound (lte/lt) in the same clause; an open-ended range " +
                "would require enumerating an unbounded number of buckets (ADR-096).");

        var config = field.SearchableConfig ?? throw new InvalidOperationException($"FilterableField \"{field.JsonPath}\" is EncryptedRangeBucket but has no SearchableConfig.");
        var granularities = RangeBucketing.OrderCoarsestFirst(config.BucketGranularities ?? [], field.DataType);
        if (granularities.Count == 0)
            return [];
        var finestGranularity = granularities[^1]; // narrow using the finest configured granularity for the tightest candidate set

        const int maxBuckets = 10_000; // a real, explicit safety bound -- see RangeBucketing.EnumerateBucketLabels
        var bucketLabels = field.DataType == FilterableFieldType.DateTimeOffset
            ? RangeBucketing.EnumerateBucketLabels(
                DateTimeOffset.Parse(lower, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal),
                DateTimeOffset.Parse(upper, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal),
                field.DataType, finestGranularity, maxBuckets)
            : RangeBucketing.EnumerateNumericBucketLabels(
                double.Parse(lower, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(upper, System.Globalization.CultureInfo.InvariantCulture),
                finestGranularity, maxBuckets);

        var resolved = await searchIndexKeyService.ResolveAsync(appId, eventTypeName, field.JsonPath, ct);
        if (resolved is null)
            return [];
        var (keyReference, backend) = resolved.Value;

        var tokens = new List<string>();
        foreach (var label in bucketLabels)
            tokens.Add(Convert.ToBase64String(await backend.ComputeHmacAsync(keyReference, Encoding.UTF8.GetBytes(label), ct)));

        var candidateSequenceNumbers = await db.EncryptedFieldIndexEntries
            .Where(e => e.AppId == appId && e.EventTypeName == eventTypeName && e.FieldJsonPath == field.JsonPath &&
                        e.Granularity == finestGranularity && tokens.Contains(e.Token))
            .Select(e => e.StoredEventSequenceNumber)
            .Distinct()
            .ToListAsync(ct);

        // Exact-match step (ADR-098) -- the bucket lookup above only narrows
        // to "somewhere in this bucket range"; the finest configured
        // granularity's own bucket can still span values on both sides of
        // the true boundary (e.g. a Day bucket for `gte 2026-03-15T12:00`
        // includes values from earlier the same day), so an exact
        // comparison still has to run -- over ONLY this already-narrowed
        // set, never a full-table decrypt.
        if (candidateSequenceNumbers.Count == 0)
            return [];
        var lowerOp = clause.Gte is not null ? "gte" : "gt";
        var upperOp = clause.Lte is not null ? "lte" : "lt";
        var afterLower = await predicateEvaluator.EvaluateAsync(candidateSequenceNumbers, field.JsonPath, field.DataType.ToString(), lowerOp, lower, ct);
        return await predicateEvaluator.EvaluateAsync(afterLower, field.JsonPath, field.DataType.ToString(), upperOp, upper, ct);
    }

    private static Expression Combine(Expression? left, Expression right) => left is null ? right : Expression.AndAlso(left, right);

    private static Expression Constant(string textValue, FilterableFieldType dataType) =>
        FilterPredicateBuilder.BuildConstantExpression(textValue, dataType);

    // Mirrors FilterPredicateBuilder's own PropertyNameFor -- the registered
    // JsonPath's last segment names the filterable field for lookup purposes.
    private static string PropertyNameFor(FilterableField field) => JsonPathValidation.Segments(field.JsonPath)[^1];
}
