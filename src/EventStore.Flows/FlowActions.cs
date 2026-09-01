using System.Text.Json.Nodes;

namespace EventStore.Flows;

// ADR-101: every converted workflow's plain (non-task) action labels
// register to this one shared delegate -- a real audit-trail log line, not
// invented automation. Every actual side effect these steps narrate is
// already automatic elsewhere in the framework (step-up enforcement,
// delegation, fold/compensate, timeout escalation); this exists so a
// reviewer reading the diagram can trust each labeled step really did fire
// for a given entity, without the flow engine pretending to own behavior
// that isn't its own.
public static class FlowActions
{
    public static Action<JsonObject> Narrate(string flowName, string stepName) =>
        _ => Console.WriteLine($"[{flowName}] {stepName}");

    // Every converted workflow's plain action labels narrate themselves
    // verbatim (the label text IS the audit-trail message) -- this avoids
    // repeating each label twice (once as the dictionary key, once as the
    // narrated text) in every *.Flow.cs registration.
    public static IReadOnlyDictionary<string, Action<JsonObject>> NarrateAll(string flowName, params string[] labels) =>
        labels.ToDictionary(label => label, label => Narrate(flowName, label));
}
