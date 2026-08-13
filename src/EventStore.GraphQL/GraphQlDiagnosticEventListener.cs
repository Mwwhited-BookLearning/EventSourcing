using System.Diagnostics;
using EventStore.Domain.Observability;
using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Resolvers;

namespace EventStore.GraphQL;

// Direct request -- "GraphQL query volume/latency," the one ADR-088 mechanism
// list never covered (Router/Webhooks/Replication/Inbox only). HotChocolate
// ships its own OpenTelemetry TRACING integration (HotChocolate.Diagnostics'
// AddInstrumentation/AddHotChocolateInstrumentation, verified via its own
// v16 docs page before ruling it out) but that package's docs only describe
// spans, never a Meter -- METRICS still need this project's own listener,
// same "buy what covers it, build only the gap" posture as everywhere else.
//
// Extends the real abstract ExecutionDiagnosticEventListener base class
// (HotChocolate.Execution.Instrumentation), not IExecutionDiagnosticEvents
// directly -- found only by letting the compiler itself enumerate the
// interface's full member set (StartProcessing/StopProcessing are real, but
// so is a much larger ICoreExecutionDiagnosticEvents base surface no doc
// page mentioned), the same "verify before citing" discipline
// FollowSubscriptionTypeModule's own comment already establishes for this
// library, taken one step further once reflection alone proved insufficient.
public sealed class GraphQlDiagnosticEventListener : ExecutionDiagnosticEventListener
{
    private const string StartTimestampKey = "duplex.graphql.start_timestamp";
    private const string HadErrorKey = "duplex.graphql.had_error";

    // Per-field resolver timing is not this metric's concern (request-level
    // volume/latency only) -- false tells HotChocolate to skip invoking
    // ResolveFieldValue at all, the cheaper path this flag exists for.
    public override bool EnableResolveFieldValue => false;

    public override void StartProcessing(RequestContext context)
    {
        context.ContextData[StartTimestampKey] = Stopwatch.GetTimestamp();
        context.ContextData[HadErrorKey] = false;
    }

    public override void StopProcessing(RequestContext context)
    {
        if (context.ContextData.TryGetValue(StartTimestampKey, out var startObj) && startObj is long start)
        {
            var hadError = context.ContextData.TryGetValue(HadErrorKey, out var flagObj) && flagObj is true;
            var outcomeTag = new KeyValuePair<string, object?>("outcome", hadError ? "error" : "ok");
            DuplexInstrumentation.GraphQlRequestLatencyMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, outcomeTag);
            DuplexInstrumentation.GraphQlRequestOutcomes.Add(1, outcomeTag);
        }
    }

    public override void ResolverError(IMiddlewareContext context, IError error) => context.ContextData[HadErrorKey] = true;
}
