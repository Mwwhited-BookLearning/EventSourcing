namespace EventStore.LineageExport;

// ADR-068 §4/ADR-062 -- the manifest records the producing framework's own
// SemVer, so "matched version reads its own bundles" (this ADR's narrowed
// guarantee) is checkable. Reads the real assembly version Directory.Build.props'
// <Version> sets, rather than a hand-maintained constant that could drift
// from the actual package version.
public static class FrameworkVersion
{
    public static string Current { get; } = typeof(FrameworkVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
