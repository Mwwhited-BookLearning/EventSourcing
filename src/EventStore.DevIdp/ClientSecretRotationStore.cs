using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace EventStore.DevIdp;

// ADR-093 -- "the ticket-exchange shared secret" half of this ADR's Decision,
// picked back up per TODO.md's own scoping ("pick up when ticket-exchange
// credential rotation is actually needed"). OpenIddictApplicationDescriptor.
// ClientSecret is a single string per registered application (verified
// against OpenIddict's own source/docs/issue tracker before this was written
// -- no built-in multiple-simultaneously-valid-secrets mechanism exists), so
// real zero-downtime rotation needs both the CURRENT override and the
// PREVIOUS secret tracked somewhere this framework owns. Registered as a
// singleton (Program.cs), same as TicketStore -- in-process, non-persistent,
// scoped to ONE running app instance, not a `static` field: a `static`
// field here would leak one WebApplicationFactory-based test's own rotation
// into every other test class's unrelated app instance sharing the same
// test process (the exact mistake DevIdpSeeder.GetClientSecret's own
// comment now warns against).
public sealed class ClientSecretRotationStore
{
    private readonly ConcurrentDictionary<string, string> _currentOverrides = new();
    private readonly ConcurrentDictionary<string, (string Secret, DateTimeOffset ExpiresAt)> _previousSecrets = new();

    // DevIdpSeeder.GetClientSecret's own seed-time value stays authoritative
    // until a rotation actually happens for that clientId; this only ever
    // returns non-null once /oauth/clients/{clientId}/rotate-secret has run
    // at least once against THIS app instance.
    public string? CurrentOverrideOrNull(string clientId) =>
        _currentOverrides.TryGetValue(clientId, out var secret) ? secret : null;

    public void SetCurrent(string clientId, string newSecret) =>
        _currentOverrides[clientId] = newSecret;

    public void RecordPrevious(string clientId, string previousSecret, TimeSpan overlapWindow) =>
        _previousSecrets[clientId] = (previousSecret, DateTimeOffset.UtcNow.Add(overlapWindow));

    // Constant-time comparison -- this compares a caller-presented secret
    // against a locally-held plaintext value, the same class of comparison
    // OpenIddict's own ValidateClientSecretAsync performs internally
    // (there, against a stored hash); an ordinary == here would leak timing
    // information about how many leading bytes matched.
    public bool MatchesUnexpiredPrevious(string clientId, string presentedSecret) =>
        _previousSecrets.TryGetValue(clientId, out var previous)
        && previous.ExpiresAt > DateTimeOffset.UtcNow
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(previous.Secret), Encoding.UTF8.GetBytes(presentedSecret));

    // The introspection endpoint needs the actual previous secret VALUE
    // (to recompute an HMAC against it), not a yes/no comparison against a
    // caller-presented candidate -- a distinct access pattern from
    // MatchesUnexpiredPrevious above, so it gets its own method rather than
    // overloading that one's contract. Returns "" (never a valid HMAC key
    // any real secret would collide with) when there's no unexpired
    // previous secret for clientId, so a caller can pass the result
    // straight into a signature comparison with no separate null check.
    public string PreviousOrEmpty(string clientId) =>
        _previousSecrets.TryGetValue(clientId, out var previous) && previous.ExpiresAt > DateTimeOffset.UtcNow
            ? previous.Secret
            : "";
}
