using System.Text.Json;

namespace EventStore.Ucan;

// ADR-107 -- symmetric with ADR-104's UcanDelegationRevoked: "who granted
// what to whom" becomes a real, queryable Event Log entry, not only ever
// reconstructable from the signed delegation JWT itself. UcanDelegation.
// Create stays a pure, synchronous, fully-offline JWT-signing function,
// UNCHANGED -- this is a separate, explicitly opt-in step a granter's own
// application calls afterward, over the ordinary Publish API, only when it
// has connectivity and wants the audit trail. Recording issuance is never
// a precondition for a delegation's own validity -- UcanValidator.
// ValidateAsync never consults this event -- the same "true-offline
// break-glass access is unaffected" invariant ADR-104's own Consequences
// already named for revocation.
//
// An ordinary, application-registered event type (the same shared,
// caller-registered posture Vitals/Meridian's own "authorityDecision"
// type already establishes), not a platform-bootstrapped reserved one --
// EventStore.Ucan has no SchemaRegistryService/PublishService dependency
// at all (a deliberate, low-level, dependency-light crypto library), so
// registration/publishing is the CALLER's own responsibility. The
// constants and payload builder below exist so every real granter
// application builds the identical shape, not just this repo's own tests.
public static class UcanDelegationIssuedEventType
{
    public const string Name = "ucanDelegationIssued";

    // EntityIdField below is "$.GrantRef" -- the delegation's own stable
    // "jti" identity (UcanDelegation.cs's own comment: "ADR-045's own
    // AccessLogEntry.GrantRef"), the same identifier a future
    // UcanDelegationRevoked event would key its own revocation lookup on,
    // once that (still entirely unbuilt -- see ADR-107's own honest
    // note) mechanism actually lands.
    public const string Schema = """
        {
          "type": "object",
          "properties": {
            "GranterActorId": { "type": "string" },
            "GranteeActorId": { "type": "string" },
            "Capabilities": { "type": "array", "items": { "type": "string" } },
            "GrantRef": { "type": "string" },
            "ExpiresAt": { "type": "string" }
          },
          "required": ["GranterActorId", "GranteeActorId", "Capabilities", "GrantRef", "ExpiresAt"]
        }
        """;

    // Builds the raw JSON `payload` TEXT this event type's own schema
    // above expects -- the same "Payload is raw JSON text, not a nested
    // object" wire shape every publish call in this design uses
    // (docs/changes/2026-09-04.md's own OpenAPI-contract fix). Takes
    // plain claim strings, not DelegatedCapability records -- trivially
    // callable from anywhere without forcing a caller to already have a
    // DelegatedCapability list assembled in that exact shape.
    public static string BuildPayload(
        string granterActorId, string granteeActorId, IReadOnlyList<string> capabilityClaims, Guid grantRef, DateTimeOffset expiresAt) =>
        JsonSerializer.Serialize(new
        {
            GranterActorId = granterActorId,
            GranteeActorId = granteeActorId,
            Capabilities = capabilityClaims,
            GrantRef = grantRef.ToString(),
            ExpiresAt = expiresAt.ToString("O"),
        });
}
