[← ADR index](../07-adrs.md)

# ADR-038: Compatibility & deployment discipline — Tolerant Reader, Expand/Contract, N-1/N+1 window

Status: Accepted

Context: This design has practiced Tolerant Reader-adjacent behavior
throughout (`ADR-022`'s `Extensions` bag, `ADR-023`'s persist-everything
posture) without ever stating the *deployment*-level discipline that
makes rolling upgrades/rollbacks actually safe. `docs/design-docs/11`
names this fully; this ADR adopts it.

Decision:
- **Wire format rule: ignore unknown fields, never remove or rename one.**
  `System.Text.Json`'s default behavior already does this — verified,
  not assumed, as a tested contract requirement for every client, not
  just an accidental default. A field is only ever *added* or marked
  deprecated-but-still-emitted (`DeprecatedAt`, `docs/data/schema-
  registry.md`) for at least one full deprecation window.
- **Enum values get an explicit unknown-fallback contract** — the single
  most common accidental breaking change in otherwise-careful systems is
  adding an enum value that an old client's exhaustive `switch` doesn't
  handle. Every enum-like field in the schema registry and GraphQL
  schema (`ADR-037`) declares a fallback: either the wire format includes
  a raw string alongside the enum (`status: "newValue", statusKnown:
  false`), or client deserialization is required to default an unknown
  value to a designated `Unknown` member rather than throwing.
- **Version discovery for mixed deployments**: a lightweight capability-
  negotiation handshake (client declares supported schema
  version(s)/feature flags at connection start) plus self-describing
  payloads (every event/entity already carries `SchemaVersion`,
  `docs/data/`) — a client can always introspect what it actually
  received.
- **Mid-capture continuity, no forced renegotiation**: because every
  event is self-describing and stateless per-message (`ADR-011`'s
  `eventId`, `ADR-020`'s `schemaVersion`, `ADR-024`'s `ExpectedVersion`
  all travel with it), a rolling deploy never needs session affinity to
  a specific server instance or code version. The router/fold step must
  accept and correctly process events tagged with *any* schema version
  the current code knows an upcaster for — restating `ADR-018`'s
  tolerance as a deployment-time guarantee, not just a data-quality one.
- **N-1/N+1 compatibility window**: any server version must correctly
  process events tagged with the immediately-previous and immediately-
  next schema version, at minimum — cutover is never atomic. Concretely:
  never delete an upcaster (`ADR-018`) or a schema version's definition
  the moment a new version ships — deprecate, keep functioning for at
  least one full deployment/rollback cycle.
- **Expand/Contract (Parallel Change) migrations, database-level**:
  **Expand** — add new nullable columns/tables, never alter or drop
  existing ones. **Migrate** — new code writes to new structures; old
  code keeps working unaffected. **Contract** (optional, much later) —
  remove old structures only once certain no rollback depends on them;
  given this design's "never lose data" principle (`README.md`),
  contraction may simply never happen for some structures. If every
  migration is expand-only, rolling back the server binary is just
  redeploying the old executable — the database is still in a shape the
  old code fully understands.
- **A rolled-back deployment doesn't lose newer-schema events**: an event
  tagged with a schema version a rolled-back deployment doesn't know
  sits as `received` (`ADR-023`'s status envelope already separates
  "durably persisted" from "successfully routed") — unroutable-but-
  persisted is always a safe, recoverable state, never data loss,
  waiting for a future deployment that reintroduces that version's
  support.
- **Feature flags as a faster lever than binary rollback** — gate new
  schema/routing/view-definition behavior behind a runtime config flag,
  complementary to (not a replacement for) the rollback safety above,
  so a bad rollout can be disabled instantly rather than requiring a full
  redeploy-rollback cycle.

Consequences:
- This is the ADR that makes `ADR-023`'s persist-everything posture and
  `ADR-018`'s upcast chain into an actual *deployment* safety net, not
  just a data-correctness one — the same mechanisms, a different, real
  payoff stated explicitly.
- `08-build-plan.md` needs an explicit exit criterion tied to this: a
  rollback drill (deploy a schema version, roll back, confirm no data
  loss and the event becomes routable again once re-forward-deployed) —
  not yet added, flagged as propagation debt.
- No new mechanism is introduced here that this design didn't already
  have a piece of — this ADR is primarily about **naming the discipline
  explicitly** (Tolerant Reader, Postel's Law, Expand/Contract, N-1/N+1)
  so it's enforced as policy, not an accidental byproduct of other
  decisions.
