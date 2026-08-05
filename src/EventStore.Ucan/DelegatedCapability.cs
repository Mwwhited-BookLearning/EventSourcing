namespace EventStore.Ucan;

// ADR-043 -- a subset of what the granter currently holds, optionally
// restricted to one specific EntityId ("this patient's record," not
// blanket clearance). EntityScope null means unscoped -- applies wherever
// the underlying claim would ordinarily apply, ADR-043's own "unaffected,
// default case."
public record DelegatedCapability(string Claim, string? EntityScope);
