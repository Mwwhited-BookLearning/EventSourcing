namespace EventStore.ViewRegistry;

public abstract record RegisterViewDefinitionResult
{
    public sealed record Success(int Version, string Hash) : RegisterViewDefinitionResult;

    public sealed record ValidationFailed(IReadOnlyList<string> Errors) : RegisterViewDefinitionResult;

    private RegisterViewDefinitionResult() { }
}
