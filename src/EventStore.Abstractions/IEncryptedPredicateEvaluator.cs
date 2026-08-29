namespace EventStore.Abstractions;

// ADR-098 -- the exact-match decrypt-and-compare step for an
// EncryptedRangeBucket clause, run ONLY over the already-narrowed candidate
// set a bucket-token lookup produces (ADR-096) -- never a full-table
// decrypt. The default implementation (AppTierEncryptedPredicateEvaluator,
// EventStore.Erasure) runs in the application tier; a future per-provider
// native implementation (SQLCLR/PostgreSQL, ADR-098) would run the same
// decrypt-and-compare inside the database engine instead, without changing
// this contract.
public interface IEncryptedPredicateEvaluator
{
    // candidateSequenceNumbers is the bucket-narrowed set from
    // EncryptedFieldIndexEntry; returns the subset whose decrypted value at
    // fieldJsonPath actually satisfies comparisonOperator/comparisonValue.
    // dataTypeName is FilterableFieldType's own name (a plain string here so
    // this interface doesn't need to reference EventStore.Domain just for
    // one enum) -- "String" | "Number" | "Boolean" | "DateTimeOffset".
    Task<IReadOnlyList<long>> EvaluateAsync(
        IReadOnlyList<long> candidateSequenceNumbers,
        string fieldJsonPath,
        string dataTypeName,
        string comparisonOperator, // "gt" | "gte" | "lt" | "lte"
        string comparisonValue,
        CancellationToken ct = default);
}
