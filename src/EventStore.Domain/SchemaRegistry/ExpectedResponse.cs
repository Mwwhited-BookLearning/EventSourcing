namespace EventStore.Domain.SchemaRegistry;

// null on EventTypeDefinition.ExpectedResponse = no tracked response
// expected (default, unchanged behavior); set = a ResponseEventType event
// carrying a matching RespondsToEventId is expected within Within, watched
// by ExpectedResponseWatcher (ADR-094). v1 deliberately allows exactly one
// ResponseEventType, not a RequiredClaims-style OR-of-list -- extend to a
// list the same way ADR-050 extended RequiredClaims, if a real case asks.
public class ExpectedResponse
{
    public string ResponseEventType { get; set; } = default!;
    public TimeSpan Within { get; set; }
}
