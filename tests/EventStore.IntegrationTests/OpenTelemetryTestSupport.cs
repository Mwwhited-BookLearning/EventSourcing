using System.Diagnostics;
using System.Diagnostics.Metrics;
using EventStore.Domain.Observability;

namespace EventStore.IntegrationTests;

// "Mechanism-Level OpenTelemetry Instrumentation" (docs/08-build-plan.md,
// ADR-088) -- the first test infrastructure in this repo asserting on a
// recorded metric/Activity at all (no prior precedent to follow).
// DuplexInstrumentation.Meter/ActivitySource are process-wide singletons,
// shared across every test in this suite exactly like the real production
// mechanisms share them -- a MeterListener/ActivityListener subscribed
// here filters to ONE named instrument/Activity, never every instrument
// process-wide, so a test running concurrently with another (MSTest's own
// parallelism) never observes an unrelated test's own measurements from a
// DIFFERENT instrument; callers additionally filter recorded measurements
// by their own scenario-unique tag value (an AppId/SubscriptionId/PeerId),
// the same "give every scenario its own unique key" convention this repo's
// tests already established after item 15's own cross-scenario collision
// bug -- necessary here too, since two concurrently-running tests CAN both
// record to the SAME named instrument.
internal static class OpenTelemetryTestSupport
{
    public readonly record struct RecordedMeasurement<T>(T Value, KeyValuePair<string, object?>[] Tags) where T : struct
    {
        public bool HasTag(string key, object? value) => Tags.Any(t => t.Key == key && Equals(t.Value, value));
    }

    public static (MeterListener Listener, List<RecordedMeasurement<double>> Measurements) ListenForDoubleInstrument(string instrumentName)
    {
        var measurements = new List<RecordedMeasurement<double>>();
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == DuplexInstrumentation.Name && instrument.Name == instrumentName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) => measurements.Add(new RecordedMeasurement<double>(value, tags.ToArray())));
        listener.Start();
        return (listener, measurements);
    }

    public static (MeterListener Listener, List<RecordedMeasurement<long>> Measurements) ListenForLongInstrument(string instrumentName)
    {
        var measurements = new List<RecordedMeasurement<long>>();
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == DuplexInstrumentation.Name && instrument.Name == instrumentName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => measurements.Add(new RecordedMeasurement<long>(value, tags.ToArray())));
        listener.Start();
        return (listener, measurements);
    }

    // ADR-088's own trace exit criterion -- "a named Activity visible in
    // the collected trace output," a distinct assertion from the metric
    // passing, per that item's own explicit "not automatically covered
    // just because the metrics pass" text.
    public static (ActivityListener Listener, List<Activity> Activities) ListenForActivity(string operationName)
    {
        var activities = new List<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DuplexInstrumentation.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == operationName)
                    activities.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);
        return (listener, activities);
    }
}
