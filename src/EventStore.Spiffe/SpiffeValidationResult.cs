namespace EventStore.Spiffe;

public abstract record SpiffeValidationResult
{
    public sealed record Accepted(SpiffeId SpiffeId) : SpiffeValidationResult;

    public sealed record Rejected(string Reason) : SpiffeValidationResult;

    private SpiffeValidationResult() { }
}
