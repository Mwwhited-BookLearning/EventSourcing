namespace EventStore.TicketExchange;

// ADR-040 -- claims a resolved ticket never carries: registration-time
// (iss/aud/jti/exp/iat/nbf) or possession-proof-specific (cnf.jkt) claims
// that don't describe "who is this, what may they do," the only things a
// ticket's introspection response replays. Deliberately excluding cnf.jkt
// here (not just relying on DpopValidationMiddleware's own AuthenticationType
// check) means a ticket-resolved principal never even LOOKS DPoP-bound --
// belt and suspenders for the same "consumed one hop earlier" property.
// One shared list, checked at Ticket-issuance time (EventStore.DevIdp,
// storing subjectClaims) rather than duplicated at resolution time too.
public static class TicketClaims
{
    public static readonly HashSet<string> ExcludedClaimTypes = ["iss", "aud", "jti", "exp", "iat", "nbf", "nonce", "cnf.jkt"];
}
