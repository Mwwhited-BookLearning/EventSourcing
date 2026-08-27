using System.Globalization;
using EventStore.Domain.SchemaRegistry;

namespace EventStore.Erasure;

// ADR-096 -- computes the bucket LABEL a raw value falls into for a given
// granularity; PayloadIndexer/GraphQlFilterPredicateBuilder then HMAC that
// label the same way an Equality token is computed. The label itself is
// never stored or transmitted in the clear -- only its HMAC is -- so this
// class's own output is an intermediate, not the thing that ends up in
// EncryptedFieldIndexEntry.Token.
public static class RangeBucketing
{
    private static readonly string[] DateGranularityOrder = ["Year", "Month", "Day"];

    public static string ComputeBucketLabel(string rawValue, FilterableFieldType dataType, string granularity)
    {
        if (dataType == FilterableFieldType.DateTimeOffset)
        {
            var dto = DateTimeOffset.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            return granularity switch
            {
                "Year" => dto.Year.ToString("D4", CultureInfo.InvariantCulture),
                "Month" => dto.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                "Day" => dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => throw new ArgumentException($"Unknown DateTimeOffset bucket granularity: {granularity}"),
            };
        }

        if (dataType == FilterableFieldType.Number)
        {
            // granularity is a numeric bucket WIDTH (e.g. "10", "100"), not a
            // named unit -- the bucket label is the width-multiple the value
            // falls into, e.g. width 10 and value 105 -> "100".
            var width = double.Parse(granularity, CultureInfo.InvariantCulture);
            var value = double.Parse(rawValue, CultureInfo.InvariantCulture);
            var bucketIndex = Math.Floor(value / width);
            return (bucketIndex * width).ToString(CultureInfo.InvariantCulture);
        }

        throw new NotSupportedException($"Range bucketing only supports DateTimeOffset and Number fields (got: {dataType})");
    }

    // Every DateTimeOffset bucket label at `granularity` a value in
    // [fromInclusive, toExclusive) falls into -- used by the query side to
    // enumerate the finite candidate set a bounded range query narrows to.
    // A Number granularity enumerates by fixed width steps the same way.
    public static IReadOnlyList<string> EnumerateBucketLabels(
        DateTimeOffset fromInclusive, DateTimeOffset toExclusive, FilterableFieldType dataType, string granularity, int maxBuckets)
    {
        if (dataType != FilterableFieldType.DateTimeOffset)
            throw new NotSupportedException("This overload only supports DateTimeOffset fields.");

        var labels = new List<string>();
        var cursor = fromInclusive;
        while (cursor < toExclusive)
        {
            if (labels.Count >= maxBuckets)
                throw new InvalidOperationException(
                    $"Range query would enumerate more than {maxBuckets} buckets at granularity \"{granularity}\" -- narrow the range or use a coarser granularity.");
            labels.Add(ComputeBucketLabel(cursor.ToString("O", CultureInfo.InvariantCulture), dataType, granularity));
            cursor = granularity switch
            {
                "Year" => cursor.AddYears(1),
                "Month" => cursor.AddMonths(1),
                "Day" => cursor.AddDays(1),
                _ => throw new ArgumentException($"Unknown DateTimeOffset bucket granularity: {granularity}"),
            };
        }
        return labels.Distinct().ToList();
    }

    public static IReadOnlyList<string> EnumerateNumericBucketLabels(double fromInclusive, double toExclusive, string granularity, int maxBuckets)
    {
        var width = double.Parse(granularity, CultureInfo.InvariantCulture);
        var labels = new List<string>();
        var cursor = Math.Floor(fromInclusive / width) * width;
        while (cursor < toExclusive)
        {
            if (labels.Count >= maxBuckets)
                throw new InvalidOperationException(
                    $"Range query would enumerate more than {maxBuckets} buckets at granularity \"{granularity}\" -- narrow the range or use a coarser granularity.");
            labels.Add(cursor.ToString(CultureInfo.InvariantCulture));
            cursor += width;
        }
        return labels.Distinct().ToList();
    }

    // Coarsest-first, per ADR-096's decomposition rule -- the query side
    // prefers the coarsest granularity that still bounds enumeration
    // reasonably. Number granularities have no inherent order (they're
    // arbitrary widths), so they're returned widest-first by numeric value.
    public static IReadOnlyList<string> OrderCoarsestFirst(IReadOnlyList<string> granularities, FilterableFieldType dataType) =>
        dataType == FilterableFieldType.DateTimeOffset
            ? [.. granularities.OrderBy(g => Array.IndexOf(DateGranularityOrder, g))]
            : [.. granularities.OrderByDescending(g => double.Parse(g, CultureInfo.InvariantCulture))];
}
