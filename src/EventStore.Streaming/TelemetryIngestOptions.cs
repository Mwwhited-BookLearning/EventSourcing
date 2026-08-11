namespace EventStore.Streaming;

public class TelemetryIngestOptions
{
    // ADR-031's "a channel falling behind by more than a configurable
    // threshold" -- expressed as a multiple of the channel's own declared
    // SampleIntervalMicros (its ExpectedInterArrivalInterval), so one
    // setting scales correctly across channels with very different rates
    // rather than a single fixed microsecond constant.
    public double LagThresholdMultiplier { get; set; } = 3.0;
}
