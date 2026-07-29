[← ADR index](../07-adrs.md)

# ADR-044: Application-defined permission/grant types via per-`AppId` trust roots

Status: Accepted

Context: `ADR-043`'s delegated access grants assumed the granter is
delegating a claim the central `EventStore.DevIdp` already recognizes.
Direction received this session: an end application should be able to
publish *additional* permissions and grants of its own — custom
capability types the central identity provider was never told about —
without the core engine needing to understand or pre-register them.
This is the same "framework contains zero domain-specific knowledge"
rule `ADR-030` already states as a hard requirement, extended from
schemas/claims/masking rules to the *permission vocabulary itself*: an
application shouldn't need a central authority's cooperation to invent
its own permission type any more than it needs one to register a new
event type.

UCAN (`ADR-036`) is the right primitive for this, but the UCAN
specification **deliberately leaves one thing unresolved, on purpose**:
it defines how a delegation *chain* is verified once a root is known,
but explicitly does not define how a verifier learns *which DID counts
as the root of trust* for a given capability namespace — that's stated
as left to "applications and deployments to establish out-of-band"
([ucan.xyz](https://ucan.xyz/specification/)). This design has a real,
already-existing per-tenant scoping key (`AppId`, `ADR-030`) that's the
natural place to hang exactly that out-of-band trust decision.

Decision:
- **Each `AppId` may register one or more trusted issuer DIDs** — a
  small new registry entry, `AppTrustRoot { AppId, IssuerDid,
  Description, RegisteredAt }`, `AppId`-scoped like everything else
  (`ADR-030`). Registering a DID as a trust root for an `AppId` is what
  makes that DID authoritative for minting/delegating *that
  application's own* custom permission/capability strings — not the
  central `EventStore.DevIdp`.
- **Permission/capability types stay opaque strings to the core
  engine**, exactly like `RequiredPublishClaim`/`RequiredReadClaim`
  already are (`ADR-008`'s free-form `"type:value"` shape) — the engine
  never validates *what* a capability means, only that a presented
  UCAN's delegation chain is cryptographically valid and roots in a DID
  registered as trusted for the `AppId` the request is scoped to.
  Convention, not enforcement, keeps custom capability strings
  collision-free across applications (e.g. namespaced
  `"{appId}:{capability}"`) — the same convention-only discipline
  `ADR-008`'s claim strings already rely on.
- **An application's own service identity issues UCANs rooted in its
  own registered DID**, with no round trip to the central IdP required
  to mint a grant for its own custom permission type — this is what
  "publish additional permissions... not defined in a central
  authority" concretely means: the central IdP's role shrinks to
  verifying delegation chains and checking `AppTrustRoot` registration,
  never to being the sole issuer.
- **`ADR-043`'s delegated-grant mechanism composes directly, unchanged**:
  a "secondary opinion" grant can now delegate either a central-IdP-
  recognized claim *or* an application-defined one — same UCAN
  delegation shape, same cap-to-the-delegator's-own-level invariant,
  same exchange flow. No second grant mechanism needed for
  application-defined permissions.
- **Revocable the same way a trust root is removed, not per-token**:
  de-registering an `AppTrustRoot` entry invalidates every future
  verification against that DID for that `AppId` (existing exchanged
  JWTs already issued keep whatever lifetime they were given —
  consistent with how `ADR-040`'s ticket consumption and `ADR-043`'s
  grant revocation both already accept "the credential itself still has
  to expire or be checked live," not retroactively un-mint something
  already handed out).

Consequences:
- **The central IdP's authority becomes "verifier of trust roots,"
  not "sole issuer of permissions"** — a real, stated shift.
  `EventStore.DevIdp`'s seeded clients/scopes (`ADR-006`) remain exactly
  as they are for the framework's own operational claims
  (`events:publish`, `registry:admin`, etc.); `AppTrustRoot` is
  additive, for application-domain permissions specifically, never a
  replacement for the operational scope model.
- **A malicious or misconfigured `AppTrustRoot` registration is a real
  risk surface** — registering the wrong DID as trusted for an `AppId`
  grants that DID's holder the ability to mint arbitrary permissions
  within that application's namespace. Who may register/deregister a
  trust root needs its own gate (a `registry:admin`-adjacent scope,
  presumably) — not designed further here, flagged to
  `docs/10-open-questions.md`.
- `docs/data/schema-registry.md` gains the small `AppTrustRoot` entity.
  No change to `StoredEvent`/`EntityStoreRow` — this is a registry-side
  concept, not an event-envelope field.
- This is the same reuse discipline as `ADR-043`: no new cryptographic
  mechanism, just resolving the one thing UCAN's own spec leaves
  unresolved (root-of-trust discovery) using a primitive (`AppId`) this
  design already had.
