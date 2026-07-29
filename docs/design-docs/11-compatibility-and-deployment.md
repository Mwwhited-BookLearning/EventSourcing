# 11 — Compatibility & Deployment

## 11.1 Guiding Principle: Tolerant Reader, Additive-Only Evolution

This platform's non-breaking compatibility goal — a service returning more or less
than a client understands should never error, and the system should keep accepting
changes (even if events need upcasting) — is the **Tolerant Reader pattern** (Fowler)
applied system-wide, combined with **Postel's Law** ("be conservative in what you send,
liberal in what you accept") and **additive-only schema evolution** (only ever add,
never remove/repurpose; deprecate-and-outlive rather than delete).

This single principle, stated once, needs to be enforced consistently at every layer
already described elsewhere in this document set:

| Layer | Mechanism | See |
|---|---|---|
| API responses (client reads server data) | Ignore unknown fields; never remove/rename a field, only add or deprecate-and-retain | 11.2 |
| Event payloads (server reads client data) | Advisory schema resolution — persist regardless | 07 |
| Query responses to older clients | Client's own GraphQL query is the projection; backward schema maps for genuinely changed fields | 10, 07 §7.3 |
| Enum/discriminated values | Explicit unknown-value fallback contract | 11.3 |
| Actions/commands | Same non-blocking accept rule as patches | 04 |
| Version discovery | Capability negotiation + self-describing payloads | 11.4 |

## 11.2 Wire Format Rules

- Clients must **ignore unknown fields** rather than fail deserialization. In .NET,
  `System.Text.Json` does this by default — verify this hasn't been overridden with
  strict mode, and make it a documented, tested contract requirement for every client.
- Never remove or rename a field outright — only ever *add* fields (backward-compatible)
  or mark fields deprecated-but-still-emitted (forward-compatible) for at least one
  full deprecation window. The Schema Registry's `DeprecatedAt` column (05 §5.3)
  provides the bookkeeping; enforce as policy that deprecated fields keep being emitted
  until every known deployed client version has aged out.
- The `extensions: JSON` field (10 §10.3) is the query-layer expression of this same
  rule — unknown properties are queryable, not errors.

## 11.3 Enum Values Are a Common Accidental Breaking Change

Adding a new enum value is *forward*-breaking for old clients that `switch`
exhaustively without a default case — the single most common accidental compatibility
break in otherwise-careful systems.

**Rule:** every enum-like field in the Schema Registry and in the GraphQL schema must
declare a default/unknown fallback:

- Wire format includes a fallback string alongside the enum
  (`status: "newValue", statusKnown: false`), or
- Client-side deserialization is required to default unknown enum values to a
  designated `Unknown`/`Other` member rather than throwing.

## 11.4 Version Discovery for Mixed Deployments

For mixed client/server versions to be *reasonable* rather than merely tolerated, each
side needs at least a loose picture of what the other supports:

- **Capability negotiation** — client declares supported schema version(s)/feature
  flags at connection/session start (a small `capabilities` handshake); server adapts
  response shaping accordingly. Lighter-weight than full downcast-per-field and covers
  most practical cases.
- **Self-describing payloads** — every entity/event already carries its
  `SchemaVersion` (05 §5.1/§5.2). Extend the same discipline to the GraphQL schema
  itself: expose a `schemaVersion`/`_meta` field on every entity type so a client can
  introspect what it actually received and decide how to render it.

## 11.5 Mid-Capture Continuity — No Forced Client Renegotiation

Because the pipeline is stateless per-message (each event is self-describing:
correlation ID, schema version, expected version all travel with it — 05 §5.1), a
client mid-capture doesn't need session affinity to a specific server instance or code
version:

- A long-running capture is a sequence of independent outbox → inbox transfers (04),
  each fully self-contained. There's no server-side session state a rolling deploy
  could invalidate.
- **Rule:** the inbox/router must accept and correctly process events tagged with
  *any* schema version the server code currently knows an upcaster for — not just
  "whatever version this deployment shipped with." This restates 07 §7.2's philosophy
  as a deployment-time guarantee, not just a data-quality one.
- For genuine network-level continuity (a live streaming/long-poll connection), use
  **graceful connection draining** on deploy: new instances accept new connections;
  old instances stop accepting new work but finish in-flight requests/streams before
  terminating — standard rolling-deployment practice.

## 11.6 N-1/N+1 Compatibility Window

For a rolling deployment (old and new instances briefly coexisting, or a canary
release), state explicitly: **any given server version must correctly process events
tagged with the previous schema version and the current one, at minimum** — ideally
N-1 and N+1, since a client might run slightly old or slightly new logic relative to
the server. This is a **dual support window** — cutover is never atomic, always
overlapping.

Concretely: never delete an upcaster or a schema version's definition from the
registry the moment a new version ships. Deprecate (`DeprecatedAt`), keep functioning
for at least one full deployment/rollback cycle.

## 11.7 Rollback Without Database Restore: Expand/Contract Migrations

The Schema Registry is already additive-only by design (07). The physical database
migration discipline must follow the same rule — the **Expand/Contract (Parallel
Change)** pattern:

- **Expand** — add new nullable columns/tables for a new capability. Never alter or
  drop existing ones.
- **Migrate** — new code starts writing to new structures; old code keeps working
  unaffected because nothing it depends on changed.
- **Contract** (optional, much later) — only remove old structures once certain no
  rollback to old code is possible and no historical data depends on them. Given the
  platform's "never lose data" stance, contraction may simply never happen for some
  structures.

If every migration is expand-only, **rolling back the server binary is just
redeploying the old executable** — the database is still in a shape the old code fully
understands, because the old code never depended on anything the new deployment added.
No restore, no rebuild.

## 11.8 Rolled-Back Deployments and Newer-Schema Events

Scenario: deployment N introduces schema v4 + its upcaster; it's bad; roll back to
deployment N-1, which only knows up to v3. Events already received tagged v4 are **not
lost** — they sit as `received`, unrouted-with-full-understanding (the pipeline already
separates "durably persisted" from "successfully routed," 04 §4.1), waiting for a
future deployment that reintroduces v4 support.

**Operational guarantee:** unroutable-but-persisted is always a safe, recoverable
state; nothing is ever discarded because a deployment was rolled back. This is a direct
payoff of the inbox/event-store split (04) — rollback degrades to "some events wait a
bit longer," never to "data loss" or "forced restore."

## 11.9 Feature Flags as a Faster Lever Than Binary Rollback

Complementary, not a replacement: gate new schema/routing/view-definition behavior
behind a runtime config flag so a bad rollout can be disabled instantly (flip a flag)
rather than requiring a full redeploy-rollback cycle. Decouples *deploying* code from
*releasing* behavior.

## 11.10 Naming Summary

For reference in reviews/ADRs: **Tolerant Reader**, **Postel's Law**, **Additive-Only
Schema Evolution**, **Expand/Contract Migrations**, **N-1/N+1 Compatibility Window**,
**Self-Describing Event Versioning** (07 §7.2's decoupling of code version from data
schema version), **Persist-Before-Route** (04's inbox/event-store split, reframed here
as a rollback-safety mechanism as well as an ingestion-reliability one).
