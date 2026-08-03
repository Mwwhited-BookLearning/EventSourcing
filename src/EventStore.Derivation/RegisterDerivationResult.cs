namespace EventStore.Derivation;

public abstract record RegisterDerivationResult
{
    public sealed record Success : RegisterDerivationResult;

    public sealed record ValidationFailed(IReadOnlyList<string> Errors) : RegisterDerivationResult;

    private RegisterDerivationResult() { }
}
