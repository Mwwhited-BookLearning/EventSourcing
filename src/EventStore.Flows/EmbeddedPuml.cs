using System.Reflection;

namespace EventStore.Flows;

// ADR-101: every converted workflow's *.Flow.cs reads its own real,
// embedded .puml the same way -- factored out once nothing else, no
// per-workflow duplication of the resource-stream plumbing.
public static class EmbeddedPuml
{
    public static string Read(Assembly assembly, string logicalName)
    {
        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded resource \"{logicalName}\" not found in assembly \"{assembly.GetName().Name}\".");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
