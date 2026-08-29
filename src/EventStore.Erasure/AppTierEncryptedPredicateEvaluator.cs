using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EventStore.Abstractions;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Erasure;

// ADR-098 -- the default IEncryptedPredicateEvaluator: decrypts ONLY the
// already-narrowed candidate rows a bucket-token lookup produced (ADR-096),
// never a full-table decrypt -- this is what "as much in-database as
// possible" actually reduces to for the bucketed-range path, since the
// bucket lookup itself already ran as an ordinary indexed DB query and only
// the small remainder needs application-tier decryption at all.
public class AppTierEncryptedPredicateEvaluator(EventStoreContext db, ErasureKeyService erasureKeyService) : IEncryptedPredicateEvaluator
{
    public async Task<IReadOnlyList<long>> EvaluateAsync(
        IReadOnlyList<long> candidateSequenceNumbers, string fieldJsonPath, string dataTypeName,
        string comparisonOperator, string comparisonValue, CancellationToken ct = default)
    {
        if (candidateSequenceNumbers.Count == 0)
            return [];

        var candidates = await db.Events
            .AsNoTracking()
            .Where(e => candidateSequenceNumbers.Contains(e.SequenceNumber))
            .Select(e => new { e.SequenceNumber, e.EntityId, e.Payload })
            .ToListAsync(ct);

        var matches = new List<long>();
        var segments = JsonPathValidation.Segments(fieldJsonPath);
        foreach (var candidate in candidates)
        {
            var plaintext = await DecryptFieldAsync(candidate.EntityId, candidate.Payload, segments, ct);
            if (plaintext is not null && Satisfies(plaintext, dataTypeName, comparisonOperator, comparisonValue))
                matches.Add(candidate.SequenceNumber);
        }
        return matches;
    }

    private async Task<string?> DecryptFieldAsync(string entityId, string payloadJson, IReadOnlyList<string> segments, CancellationToken ct)
    {
        var payload = JsonNode.Parse(payloadJson);
        JsonNode? current = payload;
        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current) || current is null)
                return null;
        }
        if (current is not JsonValue value || !value.TryGetValue<string>(out var ciphertextBase64))
            return null; // not a classified/encrypted leaf, or already erased ({"erased":true} shape) -- neither can be compared

        var resolved = await erasureKeyService.ResolveAsync(entityId, ct);
        if (resolved is null)
            return null;

        var (keyReference, backend, erased) = resolved.Value;
        if (erased)
            return null;

        var plaintextBytes = await backend.DecryptAsync(keyReference, Convert.FromBase64String(ciphertextBase64), ct);
        if (plaintextBytes is null)
            return null; // destroyed between resolve and decrypt -- treat as unreadable, same as `erased` above

        var decryptedNode = JsonNode.Parse(Encoding.UTF8.GetString(plaintextBytes));
        return decryptedNode?.ToJsonString().Trim('"');
    }

    private static bool Satisfies(string plaintext, string dataTypeName, string comparisonOperator, string comparisonValue)
    {
        int comparison = dataTypeName switch
        {
            "Number" => double.Parse(plaintext, CultureInfo.InvariantCulture).CompareTo(double.Parse(comparisonValue, CultureInfo.InvariantCulture)),
            "DateTimeOffset" => DateTimeOffset.Parse(plaintext, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
                .CompareTo(DateTimeOffset.Parse(comparisonValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)),
            _ => string.CompareOrdinal(plaintext, comparisonValue),
        };
        return comparisonOperator switch
        {
            "gt" => comparison > 0,
            "gte" => comparison >= 0,
            "lt" => comparison < 0,
            "lte" => comparison <= 0,
            _ => throw new ArgumentOutOfRangeException(nameof(comparisonOperator), comparisonOperator, "expected gt/gte/lt/lte"),
        };
    }
}
