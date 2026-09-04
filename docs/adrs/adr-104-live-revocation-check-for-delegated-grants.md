[← ADR index](../07-adrs.md)

# ADR-104: Live revocation check for delegated UCAN grants, alongside offline self-verification

Status: Accepted

Context: `ADR-043`'s own Decision section carries a correction (found
2026-08-12) stating plainly that the built `UcanDelegation` mechanism has
**no revocation-before-expiry mechanism whatsoever** — a delegation is
valid until its own `exp` claim passes, full stop. That same ADR's
Consequences section, never updated to match, still asserts revocation
checking as an existing "operational requirement... same... `ADR-040`'s
ticket consumption already has" — an internal contradiction between the
two sections this ADR resolves directly, not just a documentation gap:
`docs/10-open-questions.md` row 2 tracked whether this was an accepted
trade-off or a real gap to close. Direct request resolves it: "grants
should be validated on the server ti[m]e to check for a revocation."

Decision:
- **Offline self-verification is unchanged** — signature validity,
  proof-chain/trust-root check, and attenuation (`ADR-043`'s own cap
  invariant) all still verify with zero network calls, exactly as today.
  This decision adds one more check, it does not replace the offline
  ones.
- **A live revocation check is added at the same choke point every
  delegation already passes through**: `UcanValidator.ValidateAsync`
  (`EventStore.Ucan`) gains a query against a server-side revocation
  record, keyed by the delegation's own unique identifier, consulted
  *in addition to* the existing offline checks — a delegation that
  passes every offline check but is found revoked still fails
  validation.
- **Reuses this design's own already-adopted mechanism rather than
  inventing a new one**: `ADR-040`'s RFC 7662-shaped introspection
  pattern (already used for ticket consumption) is the concrete shape
  this reuses — a live status check consulted at the point of use,
  the same "still-unexpired-but-revoked must still fail" requirement
  `ADR-043`'s own (previously unfulfilled) Consequences text already
  named. This is the same well-established hybrid **CRL/OCSP** shape
  real-world PKI uses for X.509 certificates — offline-verifiable by
  default, with a live status check layered on top for revocation
  specifically (RFC 5280's CRL profile; RFC 6960's OCSP, the lighter-
  weight live-query alternative to a full list) — verified directly,
  not assumed, as genuine, applicable prior art for exactly this
  "self-verifying credential, plus a live revocation check" shape.
  Nothing here adopts X.509/PKI itself — only the general pattern is
  reused, via the OAuth-native mechanism (`RFC 7662`-shaped
  introspection) this design already has.
- **The revocation record is a reserved event, not a bespoke list
  store** — reuses `ADR-067`'s existing "control-plane action as a
  reserved event" convention: a granter revokes by publishing a
  `UcanDelegationRevoked` event (real, hash-chained, auditable, per this
  design's own governing "never lose data" principle) naming the
  delegation's own identifier. A lightweight, queryable fold of these
  events (or a direct query against the Event Log, whichever proves
  simpler when this is actually built) is what `UcanValidator` consults.
  **Issuance itself is deliberately NOT made an event by this decision**
  — that was `ADR-043`'s own separate, already-corrected finding (no
  `accessGrant`/`accessGrantRevoked` event type exists, and this ADR
  doesn't reopen that); only *revocation* gets a real event trail here,
  narrowly matching what was actually asked for.
- **This is a design decision only — no code changes this pass.** This
  session is design-phase-only; `UcanValidator.ValidateAsync`'s actual
  revocation-query implementation, the exact delegation-identifier field
  it keys on, and the fold/query mechanism for `UcanDelegationRevoked`
  are all real implementation work for whenever code work resumes, not
  decided down to the field name here.

Consequences:
- **Resolves `docs/10-open-questions.md` row 2** — no longer an open
  trade-off; live revocation checking is the accepted design, offline-
  only self-verification is not.
- `ADR-043`'s own Consequences bullet ("Revocation before natural
  expiration relies on the IdP actually checking revocation status...")
  is no longer aspirational text describing an unmet requirement — this
  ADR is the mechanism that fulfills it. A cross-reference to `ADR-104`
  belongs in `ADR-043`'s own text (added in the same pass as this ADR).
- `docs/patterns/delegated-capped-time-boxed-access-grants.md` and
  `docs/patterns/self-attested-did-ucan-delegation.md` both currently
  state "no revocation-before-expiry mechanism exists" in their own Cost
  sections — both need a cross-reference to this ADR (added in the same
  pass).
- **A real, honest trade-off, not hidden**: this reintroduces exactly
  the network round-trip DID/UCAN's own offline-verifiability was
  originally valued for avoiding (`ADR-036`'s own reasoning) — a
  validator now needs connectivity to check revocation status, even
  though signature/attenuation checking itself still doesn't. True-
  offline break-glass access (`ADR-043`'s own composed consequence, zero
  upstream contact) is unaffected in principle — a fully offline
  validator simply cannot see a revocation that happened after its last
  sync, the same latency window any CRL-based offline verifier already
  accepts as a known, named limitation, not a defect introduced here.
- `references.md` gains RFC 5280/RFC 6960 as reference-only prior-art
  citations (the general pattern, not adopted mechanisms themselves —
  RFC 7662, already adopted via `ADR-040`, is the actual mechanism
  reused).

**Built, 2026-09-04.** This ADR's own "design decision only" bullet above
was left unbuilt through the entire 2026-09-02→03 design-phase-only
window and was only discovered still unbuilt while landing `ADR-107`
(the sibling issuance-audit ADR) — flagged there honestly, tracked in
`TODO.md`, and closed here for real:
- `UcanDelegationRevoked` is a genuinely platform-**reserved** event
  (`src/EventStore.Rbac/UcanDelegationRevokedEventType.cs`), unlike
  `ADR-107`'s deliberately-ordinary `ucanDelegationIssued` — matching
  this ADR's own explicit "reuses `ADR-067`'s... reserved event"
  framing. `POST /ucan/delegations/{grantRef}/revoke`
  (`EventStore.Rbac/RbacEndpoints.cs`), gated the same
  `registry:admin`/`AppIdScopeEvaluator.CanAdminister` tier
  `RoleRevokedEventType`'s own endpoint already uses, is the real
  granter-facing revoke call.
- The fold/query mechanism this ADR left open ("a lightweight,
  queryable fold of these events... whichever proves simpler") landed
  as the fold: `RbacProjectionWorker` (already tailing `RoleGranted`/
  `RoleRevoked`/`PermissionGranted`/`AppTrustRootRegistered` into
  DevIdp's own local tables) now also tails `UcanDelegationRevoked`
  into a new local `RevokedDelegations` table
  (`RevocationService`/`RevokedDelegation.cs`) — the same "DevIdp keeps
  its own queryable copy, populated by Follow, never a live dependency
  on any Host's own `EventStoreContext`" posture `TrustRootService`/
  `AppTrustRoot` already established for `ADR-044`.
- `UcanValidator.ValidateAsync` gained a new, optional `Func<Guid,
  Task<bool>> isRevoked` parameter, consulted by `GrantRef` (the
  delegation's own `jti`) after every existing offline check passes —
  a delegation found revoked fails with `"delegation has been
  revoked"`, exactly this ADR's own "still-unexpired-but-revoked must
  still fail" requirement. Wired into the one real call site,
  `EventStore.DevIdp/Program.cs`'s `/connect/token` UCAN-exchange
  branch, backed by the new `RevocationService`.
- Real verification, not a stand-in call:
  `tests/EventStore.IntegrationTests/UcanDelegationRevocationHttpSqliteTests.cs`
  issues a real delegation, exchanges it successfully, revokes it over
  real HTTP, folds the real event through the real Follow subscription
  (`RbacProjectionWorker.CatchUpOnceAsync`, the same "drive the fold
  directly, post-`ClassInit`" pattern `RbacProjectionWorkerHttpSqliteTests.cs`
  already established), and confirms the identical delegation is then
  genuinely rejected (`400`, `"delegation has been revoked"`) — plus a
  second test confirming the check is genuinely keyed by `GrantRef`,
  not a blanket per-`AppId` check. Both passing; the adjacent
  `DelegatedGrantsRbacFederationHttpSqliteTests`/
  `RbacProjectionWorkerHttpSqliteTests` suites (11 tests) confirmed
  unaffected.
