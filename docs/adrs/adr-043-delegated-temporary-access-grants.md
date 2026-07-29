[← ADR index](../07-adrs.md)

# ADR-043: Delegated, capped, time-boxed read-access grants ("secondary opinion" access)

Status: Accepted

Context: Direction received this session, described as a "four eyes"
override: a user holding special authority (e.g. a doctor with
`clearance:phi`) should be able to grant *another specific* user
temporarily elevated read access — capped at the granter's own authority
level, never broader — for cases like requesting a colleague's secondary
opinion on a guest/attested user's data.

**Naming this honestly against real prior art, rather than assuming the
requester's own term is the exact match**: the classical **Four Eyes
Principle** (also "two-person rule," "dual control") requires *two*
people to jointly approve *one* action before it proceeds (a bank
transaction sign-off, a production deployment) — that is not this
mechanism, which is one person unilaterally *delegating* access to
another, not two people jointly gating a single action. The closer real
analogue is healthcare **break-glass access** (temporary, audited,
capped emergency access to a record) — except break-glass is normally
*self-service* (the accessing clinician invokes it for themselves,
emergency-triggered); this is *peer-granted* (one specific authorized
user extends access to another specific named user, deliberately, ahead
of the need). Both are cited below; neither is adopted as-is.

Decision:
- **Reuse UCAN delegation (`ADR-036`) — this is a new use case for an
  already-adopted mechanism, not a new one.** UCAN's core invariant —
  a delegated capability can never be broader than what the delegator
  holds, provable via a self-verifying chain rooted in the delegator's
  own key — is exactly "capped to the granter's own level," already true
  by construction. Nothing new needs to enforce the cap; UCAN validation
  already rejects an attempted over-broad delegation.
- **The granter issues a UCAN delegation** naming: the grantee's DID,
  the specific claim(s) being delegated (a subset of what the granter
  currently holds — e.g. `clearance:phi`), an **entity-scope
  restriction** (one specific `EntityId` — "this patient's record," not
  blanket clearance across every patient, the actual shape of a
  secondary-opinion request), and an expiration.
- **The grantee exchanges the UCAN for an ordinary bearer JWT** via the
  exact `POST /oauth/token` Token Exchange flow `ADR-036` already
  defines (`grant_type=urn:ietf:params:oauth:grant-type:token-exchange`).
  The resulting JWT carries the delegated claim(s) plus the entity-scope
  restriction as an additional claim — downstream code sees an ordinary
  bearer JWT, same as `ADR-036`'s consequence already promises ("no
  downstream service needs to understand what a UCAN or DID even is").
- **`ADR-008`'s claim model gains a general, standing entity-scope
  dimension — row-level access, not just type/endpoint-level.** This
  isn't specific to delegated grants: *any* claim — direct (`ADR-046`),
  role-granted (`ADR-046`), or delegated (this ADR) — may optionally
  carry an `entityScope` restricting it to one specific `EntityId`
  rather than every entity of a type. The check becomes "does the
  caller have this claim, *and* does it apply to this `EntityId`" — not
  a bare `HasClaim` boolean. An ordinary, unscoped claim (the default,
  unaffected case) behaves exactly as `ADR-008` already specifies. This
  is the same problem **Row-Level Security (RLS)** solves at the
  database layer (native `CREATE POLICY` support in PostgreSQL and SQL
  Server) — cited as the named real-world pattern this generalizes, not
  adopted verbatim: this design checks entity scope at the **application/
  claims layer**, not via provider-native RLS policies, specifically
  because SQLite (`ADR-001`'s third supported provider) has no native
  RLS feature at all — the same "portable, not provider-native" instinct
  `ADR-004` already applies to JSON storage.
- **Grant issuance and revocation are ordinary events**, not a new
  persistence mechanism — an `accessGrant`/`accessGrantRevoked` event
  type, registered and folded like any other, giving delegation the
  same auditable, queryable, never-deleted trail as everything else in
  this design (`README.md`'s governing principle). The event is a record
  that a grant happened; the live authorization check itself still
  happens at UCAN exchange/introspection time (an expired or revoked
  grant fails there), not by scanning events at query time.
- **Not resolved here, flagged to `docs/10-open-questions.md`**: whether
  every *read* made under a delegated grant should itself be logged as
  an auditable event. Break-glass's own literature treats per-use audit
  logging as standard practice specifically because emergency access is
  an exception path worth watching closely — but this project's read
  side isn't event-sourced today (only writes are), so logging every
  read would be a genuinely new mechanism, not a reuse of an existing
  one. Worth its own ADR if/when it's actually built, not decided by
  default here.

**Prior art**:
- **Four Eyes Principle / two-person rule / dual control** — the
  requester's own term, disambiguated above as *not* the mechanism this
  ADR builds; cited so a reader who expected dual-approval semantics
  isn't left wondering whether it was overlooked.
- **Break-glass access** (healthcare EHR emergency access — see
  [HIPAA §164.312(a)(2)(ii)](https://hipaa.yale.edu/security/break-glass-procedure-granting-emergency-access-critical-ephi-systems)):
  the audited, capped, time-limited shape this ADR borrows the *spirit*
  of, adapted from self-service/emergency-triggered to peer-granted/
  deliberate.
- **UCAN delegation** (`ADR-036`, already adopted) — the actual
  mechanism, reused rather than reinvented.
- **Row-Level Security (RLS)** — the named database-layer pattern for
  "access control finer than table/type-level," implemented here at the
  application/claims layer instead of via provider-native `CREATE
  POLICY` support, for portability across `ADR-001`'s three providers
  (SQLite has none).

Consequences:
- No new cryptographic or delegation mechanism — this is UCAN applied to
  a new use case, consistent with this design's "prefer buy over build"/
  "never invent a bespoke mechanism when a real standard already fits"
  conventions.
- `docs/data/schema-registry.md`/`event-log.md` are structurally
  unaffected — `accessGrant` is an ordinary registered event type, not a
  new column on either.
- `ADR-008`'s claim model gains the entity-scope extension described
  above — worth a short cross-reference note in `docs/data/
  schema-registry.md`'s `RequiredReadClaim` description, not a rewrite
  of that ADR.
- Per-use read audit logging is a genuine open question, not silently
  decided either way — see `docs/10-open-questions.md`.
- Revocation before natural expiration relies on the IdP actually
  checking revocation status at each exchange/introspection call, not
  just the UCAN's own `exp` — same operational requirement `ADR-040`'s
  ticket consumption already has (a still-unexpired-but-revoked
  credential must still fail), not a new category of problem.
