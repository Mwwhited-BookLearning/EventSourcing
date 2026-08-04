using System.Collections.Concurrent;

namespace EventStore.DevIdp;

// ADR-040's own Consequences: "gains a Ticket record in its existing
// in-process, non-persistent OpenIddict-adjacent store" -- auth.md's
// existing statement that client/token state lives entirely in DevIdp,
// never EventStoreContext. A plain in-memory ConcurrentDictionary is that
// store; a ticket's whole point is being short-lived and single-use, so
// nothing here needs the durability EventStoreContext exists to provide.
public class Ticket(
    string value,
    string secretRef, // the registered client_id (client_secret path) OR the one_time_secret itself (never persisted anywhere else)
    DateTimeOffset expiresAt,
    List<(string Type, string Value)> originalTokenClaims)
{
    public string Value { get; } = value;
    public string SecretRef { get; } = secretRef;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public List<(string Type, string Value)> OriginalTokenClaims { get; } = originalTokenClaims;
    private int _consumed;

    public bool IsExpired => ExpiresAt <= DateTimeOffset.UtcNow;

    // Single-use per ADR-040 -- but ONLY on a SUCCESSFUL (signature-matching)
    // resolution: a wrong-signature presentation must NOT burn the ticket
    // for its rightful owner's own later, correctly-signed retry (the
    // introspection endpoint calls this only after its own HMAC comparison
    // already matched). Interlocked.CompareExchange makes "first successful
    // use wins" atomic against two concurrent correctly-signed presentations
    // of the same ticket racing each other.
    public bool TryMarkConsumed() => Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;
}

public class TicketStore
{
    private readonly ConcurrentDictionary<string, Ticket> _tickets = new();

    public void Add(Ticket ticket) => _tickets[ticket.Value] = ticket;

    public bool TryGet(string value, out Ticket? ticket)
    {
        if (_tickets.TryGetValue(value, out var found) && !found.IsExpired)
        {
            ticket = found;
            return true;
        }
        ticket = null;
        return false;
    }
}
