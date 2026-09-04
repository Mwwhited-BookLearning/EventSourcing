[← ADR index](../07-adrs.md)

# ADR-107: Delegated-grant issuance gets a real audit event, symmetric with `ADR-104`'s revocation event

Status: Accepted

Context: `docs/10-open-questions.md`'s last remaining row asked whether
`ADR-043`'s delegated-grant *issuance* should also get a real Event Log
audit trail (an `accessGrant`-shaped reserved event, publishing who
granted what to whom), or whether `ADR-104`'s new revocation event plus
offline signature verification already gives this enough of an audit
story. `ADR-104` deliberately did not answer this — it narrowly scoped
itself to revocation only, stating plainly "issuance itself is
deliberately NOT made an event by this decision." Direct request,
resolving the open question: "Add issuance as an event too" — issuance
should be symmetric with revocation, both real, queryable Event Log
entries.

Decision:
- **`UcanDelegation.Create` (`EventStore.Ucan`) stays completely
  unchanged** — a pure, synchronous, fully-offline JWT-signing function,
  zero network calls. This is a deliberate design invariant, not an
  oversight: `ADR-104`'s own Consequences named "true-offline break-glass
  access... zero upstream contact" as a preserved property, and issuance
  recording must not become a precondition for a delegation's own
  validity or `UcanValidator.ValidateAsync` would gain a new offline
  failure mode this design has never accepted.
- **A new, ordinary (not platform-reserved) event type,
  `ucanDelegationIssued`**, carries `GranterActorId`, `GranteeActorId`,
  `Capabilities` (the delegated claim strings), `GrantRef` (the
  delegation's own `jti`, matching `ADR-045`'s `AccessLogEntry.GrantRef`
  convention), and `ExpiresAt`. Lives in `EventStore.Ucan` itself
  (`UcanDelegationIssuedEventType.cs`) as constants + a payload builder —
  `EventStore.Ucan` has zero `SchemaRegistryService`/`PublishService`
  dependency by design (a low-level, dependency-light crypto library), so
  registration/publishing is the **caller's own responsibility**, the
  same posture Vitals/Meridian's own shared `authorityDecision` type
  already establishes for a caller-registered type reused across
  workflows — not a platform-bootstrapped reserved type the way
  `SchemaRegistered`/`ChannelLagDetected` are.
- **A real granter application records issuance as a separate, explicitly
  opt-in step**, called after `UcanDelegation.Create` succeeds, over the
  ordinary Publish API (`POST /publish/ucanDelegationIssued`) — exactly
  like publishing any other business event. Recording issuance requires
  connectivity and an `events:publish`-scoped credential; a fully offline
  granter simply doesn't get an audit trail for that specific delegation
  until connectivity returns, the same accepted latency window
  `ADR-104`'s own offline-revocation-visibility trade-off already names.
- **`UcanValidator.ValidateAsync` never consults this event.** Recording
  issuance is purely for audit/observability — it has no bearing on
  whether a delegation validates, matching `ADR-104`'s own "this is the
  same well-established... offline-verifiable by default" framing.

**Honest finding, not fixed here**: while wiring this up, `ADR-104`'s own
sibling `UcanDelegationRevoked` mechanism was found to be **entirely
unbuilt** — `ADR-104`'s own Decision text says plainly "this is a design
decision only — no code changes this pass," and a direct code search
confirms it: no `UcanDelegationRevoked` type exists anywhere in `src/`,
and `UcanValidator.ValidateAsync` has no revocation check at all. The
"grants should be validated on the server time to check for a
revocation" decision from earlier this session is still only a design,
not running code. This ADR does not build it — narrowly scoped to
issuance only, matching the directive that produced it — but it's a real,
sizeable gap worth a `TODO.md` item, not left silently undiscovered.

**Also found while building the real verification test**: the dynamic
entity-query GraphQL layer (`EventStore.GraphQL/EventTypeSchemaReader.cs`)
is deliberately scalar-only — an already-documented, pre-existing
narrowing (`08-build-plan.md`), not something this ADR's own work
introduced. `Capabilities` (an array-typed property) is silently skipped
by that layer and cannot be queried as a dynamic entity field; the new
regression test verifies the three scalar fields
(`GranterActorId`/`GranteeActorId`/`GrantRef`) instead, which is still
real, independent proof the event lands correctly.

Consequences:
- **Resolves `docs/10-open-questions.md`'s last remaining row** — deleted
  outright, per that file's own workflow.
- `ADR-043`'s own correction note (2026-08-12, "no `accessGrant`/
  `accessGrantRevoked` event type exists anywhere in `src/`") gains a
  forward-pointing addendum: the issuance half of that gap is now closed
  by this ADR, additive-history style; the revocation half remains
  genuinely open (see the honest finding above).
- A new `TODO.md` item tracks building `ADR-104`'s own
  `UcanDelegationRevoked` mechanism and the live revocation check for
  real — discovered, not invented, by this pass.
- Real verification:
  `tests/EventStore.IntegrationTests/DelegatedGrantsRbacFederationHttpSqliteTests.cs`'s
  new `IssuingADelegationCanBeRecordedAsARealQueryableAuditEvent` test
  registers the type, issues a real delegation, publishes the audit
  event over real HTTP, waits for the real `RouterWorker` fold, and
  queries the resulting entity back via GraphQL — confirmed passing, full
  test-class regression (7/7) confirmed unaffected.
