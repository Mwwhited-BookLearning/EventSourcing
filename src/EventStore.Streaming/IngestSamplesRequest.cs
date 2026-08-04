using System.Text.Json;

namespace EventStore.Streaming;

// ADR-031 -- two legal batch shapes per channel. A fixed-rate channel omits
// per-sample timestamps entirely (StartTimestamp + SampleIntervalMicros +
// a flat Values array); an irregular/event-driven channel (and every Media
// channel -- each codec frame carries its own timestamp) sends explicit
// (timestamp, value) pairs instead. Exactly one of the two shapes is
// populated per request. Value is a raw JsonElement rather than a fixed
// CLR type since its real shape depends on the channel's own ContentKind
// (a number for RawScalar, a base64-encoded blob for RawBinary/Media) --
// resolved against the channel at ingest time, not at request-binding time.
public record IngestSamplesRequest(
    DateTimeOffset? StartTimestamp,
    long? SampleIntervalMicros,
    List<double>? Values,
    List<IrregularSampleRequest>? Samples);

public record IrregularSampleRequest(DateTimeOffset Timestamp, JsonElement Value, long? MonotonicElapsedMicros = null);
