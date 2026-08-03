namespace EventStore.SchemaRegistry;

public abstract record RegisterEventTypeResult
{
    public sealed record Success(int Version) : RegisterEventTypeResult;

    public sealed record ValidationFailed(IReadOnlyList<string> Errors) : RegisterEventTypeResult;

    private RegisterEventTypeResult() { }
}
