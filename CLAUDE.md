# CLAUDE.md

This repo is a **design package**, not an implemented codebase — there is no
`src/` yet. Every file under `docs/` is architecture/decision documentation
for an event-sourcing store, meant to be built from later. Treat doc edits
with the same care as code edits: internal consistency across files matters
more here than almost anything else, because there's no compiler to catch
drift.

Note: the folder/repo name itself is a typo (`EventSouring` → should be
`EventSourcing`). Known, deliberately not yet fixed — renaming the directory
this session's tools are actively running against was judged too risky
mid-task. Confirm with the user before doing it, and do it as an isolated
step (rename, then verify the shell's cwd survived) — don't fold it into a
larger batch of unrelated changes.

## Layout

- `README.md` — entry point: what the system is, doc index, open decisions.
- `docs/01`–`09` — the core design, read in that order (C4 architecture →
  data model → API contracts → OData pushdown → schema registry → solution
  structure → ADRs → build plan → CQRS read side). `04-odata-filter-
  pushdown.md` is partway through being superseded — see Integration status.
- `docs/02-data-model.md` — **classification overview + index only**, same
  split as the two below. The entity classes themselves live one group per
  file under `docs/data/` (`schema-registry.md`, `event-log.md`,
  `entity-store.md`, `dbcontext-and-conventions.md`). Never write an entity
  class back into `02-data-model.md` — add it to the right group file and
  update the classification table/diagram if it's a new group.
- `docs/07-adrs.md` — **template + index only.** The ADRs themselves live
  one per file under `docs/adrs/adr-NNN-slug.md`. Never write ADR content
  back into `07-adrs.md` — add a row to its index table and create the file
  under `adrs/`.
- `docs/patterns/` — a **pattern reference**, distinct from an ADR (a
  specific decision) and from `references.md` (a bare bibliography line):
  explains the general pattern first (cited), then points to the ADR(s)
  that apply it here. **Keep this folder in sync as new patterns/practices
  get discovered or decided — this is a standing instruction, not a
  one-time task.** At minimum, add a row to `patterns/README.md`'s catalog
  the same turn a new pattern gets decided; write the full standalone doc
  (with a PlantUML/Salt diagram, whichever fits) when there's time to do it
  properly. Falling behind on this folder has already happened once —
  don't let it happen again.
- `docs/features/*.md` — one doc per feature: context, PlantUML
  sequence/ER diagrams, Gherkin scenarios. Extracted into real `.feature`
  files only once implementation starts (see `06-solution-structure.md`).
- `docs/references.md` — bibliography. Two sections: standards actually
  **adopted** (with which ADR/doc uses them), and standards **considered
  and rejected**, each with the specific reason. Every new ADR that leans on
  a real-world spec/library should get an entry here, and every rejection
  should be as explicit as an adoption — don't let something get evaluated
  and then silently disappear. **A rejection can later flip to adopted** if
  a new requirement removes the original reason for rejecting it — this has
  already happened once (content-addressable storage: rejected for lack of
  a large-object use case, re-adopted once `ADR-032`'s binary attachments
  created one). Check `references.md` for a stale rejection before assuming
  something is still out of scope.
- `docs/design-docs/` — a **second, independently-developed design**
  (a distributed, entity-centric event-sourced platform) that is currently
  being merged into the primary design above. See "Integration status"
  below before assuming anything in `01`–`09`/`adrs/`/`data/` reflects the
  final state — several foundational pieces have already changed, more
  than once.

## Conventions established so far

- **ADRs are additive history, not editable state.** When a later decision
  changes an earlier one, don't delete the old text — strike it through
  (`~~...~~`) and add "Superseded by `ADR-XXX`" (see `ADR-006`'s
  `access_token` workaround for the pattern). If the earlier ADR was written
  *this same integration effort* and never shipped/built, a clean rewrite in
  place is fine instead (see `ADR-018`'s upcast mechanism, revised in place
  before anything depended on the original version — and `ADR-031`, revised
  in place from "telemetry channels" to "streaming channels" once
  audio/video turned out to be the same mechanism, before anything
  downstream depended on the narrower framing).
- **Never hardcode a future ADR's number in a propagated doc.** Write "the
  queued replication ADR" / "(queued sharding ADR)", not "`ADR-027`", for
  anything not yet actually written under `docs/adrs/`. ADR numbers are
  assigned by write order, and something has jumped the not-yet-written
  queue *repeatedly* this session (once when API-docs-UI/Aspire+OTel landed
  ahead of the design-docs merge queue, again when upcast-materialization/
  downcast/logical-fold/multi-tenancy/streaming-channels/attachments did) —
  every hardcoded forward reference written before that churn had to be
  found and fixed. Don't add more of them. Backfill the real number into
  cross-referencing docs only once an ADR is actually written.
- **Verify a spec before citing it.** Every RFC/standard number cited in an
  ADR was confirmed against the real spec (WebFetch the datatracker/spec
  page) before being written down, not recalled from memory and assumed
  correct — this includes pattern names in `docs/patterns/` (Idempotent
  Receiver, Materialized View, Optimistic Offline Lock, Tolerant Reader,
  Watermarks/event-time, Media Fragments URI, HTTP Range Requests were all
  confirmed this way, not recalled).
- **Never invent a bespoke mechanism when a real standard already fits.**
  Check whether an existing RFC/spec/library already solves it, adopt it if
  the fit is genuine, and explicitly record in `references.md` why *not* if
  it was considered and rejected.
- **Say when something is only partially borrowed**, and disambiguate
  terminology collisions explicitly rather than hoping context makes it
  clear (examples: "query parameter" vs. the HTTP `QUERY` method, `ADR-010`;
  "projection" meaning a CQRS read model vs. design-docs' schema-mapping
  sense, `ADR-018`).
- **A repeated relationship gets its own envelope-metadata field, never
  conflated with an existing one just because the shape looks similar.**
  This design now has four: `parentEventIds` (causal derivation, `ADR-005`),
  `MaterializationOfEventId` (reshaped copy of, `ADR-027`),
  `TelemetryPointer` (position in a signal/media stream, `ADR-031`), and
  `AttachmentRef` (supporting binary content, `ADR-032`). If a fifth comes
  up, ask what question it specifically answers before reusing one of these
  four.
- **A new capability gets a Phase in `08-build-plan.md`**, with real
  dependencies and concrete exit criteria tied to a feature doc's Gherkin
  scenarios — not just an ADR with no build-plan entry. (Build-plan
  propagation for everything past `ADR-024` is currently behind — see
  Integration status.)

## Standing requirements not yet attached to a written ADR

Real constraints already stated by direction, to be honored by whichever
ADR eventually covers the mechanism they apply to — don't lose these while
that ADR is still queued:

- **Any outbox this design introduces (peer-sync replication, or the MVVM
  client's local outbox) must be fault/abend/restart-tolerant** — durable
  and resumable across an unclean process termination, not merely safe
  under a graceful shutdown. Applies to the queued replication ADR and to
  `ADR-033` (MVVM client) once either is written.

## Integration status (`docs/design-docs/` → primary design)

In progress, sequenced as ADRs `021` onward. Big, foundational, locked-in
decisions (don't re-litigate without a strong reason — these came from
explicit direction):

- **Full entity-centric rebuild** — `EntityId`, an always-on Entity Store,
  `ExpectedVersion` (`ADR-021`).
- **Persist-everything ingestion** — publish returns `202` + a status
  envelope; schema/authority problems become advisory flags, not `400`s
  (`ADR-023`, superseding parts of `ADR-011`/`013`/`020`).
- **Distribution is in scope** — sharding + multi-origin replication will be
  built out (queued, not yet written — see below), not deferred the way
  `ADR-007` is.
- **GraphQL replaces OData entirely** — not "primary/secondary," a full
  swap (queued — see below). GraphQL queries travel over HTTP `QUERY`, not
  `GET`, specifically to keep PII/PHI-bearing filter arguments out of
  URLs/logs/proxy caches. Supersedes `ADR-003`/`04-odata-filter-
  pushdown.md`'s OData surface (the per-provider JSON pushdown mechanism
  survives; only the OData syntax goes away) and will move `ADR-018`'s
  upcast mechanism off OData `compute()` onto JS/CEL + GraphQL SDL
  directives once written.
- **This is a multi-tenant framework, not a single application** —
  `appId` is a real scoping key on the schema registry, not just a prefix
  convention (`ADR-030`). The `Orders` walkthrough is a sample application
  consuming the framework, not part of it.

**Landed, independent of the entity/persist-everything direction**
(tooling/infrastructure, decided alongside the merge but not part of it):
`ADR-025` (Scalar for OpenAPI docs UI, `@asyncapi/react-component` for
AsyncAPI) · `ADR-026` (dev via .NET Aspire + full OpenTelemetry — logging,
tracing, metrics; prod via Docker Compose, elevated from `ADR-006`'s
original "fallback" framing).

**Landed, extending the schema-evolution/data story further than the
original design-docs merge asked for** (organic growth mid-session, not
originally scoped): `ADR-027` (materialized upcasts, persisted to the log,
folded exactly once — never both) · `ADR-028` (downcast on retrieval for
an explicitly older requested version, read-time only, never
materialized) · `ADR-029` (logical-order fold using `OccurredAt`, not
arrival order, so late-arriving events don't corrupt newer data —
`LateArrivalFlag`, `Version` vs. `LastAppliedSequenceNumber` now two
different counters) · `ADR-031` (streaming channels — telemetry *and*
audio/video, a separate fast path bypassing schema validation/hash-chain/
Entity-Store-fold entirely, linked back to ordinary domain events via
`TelemetryPointer`; playback via HTTP Range Requests, deep-linking via
W3C Media Fragments URI, redaction as a new range-based primitive distinct
from `ADR-009`'s masking) · `ADR-032` (binary attachments, content-
addressed, linked via `AttachmentRef`).

**Done and propagated:** `ADR-021`–`024` fully reflected in `docs/data/`
and `03-api-contracts.md`. `ADR-025`/`026` still need their propagation
pass into `06-solution-structure.md` (`ServiceDefaults`' OTel wiring, the
`Scalar`/`asyncapi-ui` routes — `06-solution-structure.md` has the compose
vs. Aspire framing fix but not the OTel code sketch yet) and `references.md`
(Scalar/asyncapi-react/OpenTelemetry citations are in, Docker Compose's
is in). `ADR-027`–`032` are written but **not yet propagated anywhere**
beyond `docs/data/`'s field-level additions and the ADR index — no C4,
solution-structure, build-plan, or feature-doc updates yet for any of them.

**Queued, not yet written — deliberately no numbers assigned** (see the
"never hardcode a future number" convention above): replication
(`OriginId`/`LogicalClock`, peer sync outbox/inbox — must be fault/abend/
restart-tolerant per the standing requirement above) · sharding ·
non-authoritative capture/`AuthorityStatus` · DID/UCAN + OAuth Token
Exchange RFC 8693 (un-rejecting what `references.md` marks reference-only,
once the non-authoritative-capture ADR creates the need) · GraphQL-only
query layer (retargets `ADR-012`'s `QUERY` method, supersedes
`ADR-003`/`04-odata-filter-pushdown.md`, moves `ADR-018` onto JS/CEL) ·
compatibility/deployment discipline (Tolerant Reader, Expand/Contract,
N-1/N+1 window) · MVVM client + entity view definitions (the least
load-bearing piece for everything else — fine to sequence last).

Before assuming `01-c4-architecture.md`, `06-solution-structure.md`,
`08-build-plan.md`, or `04-odata-filter-pushdown.md` are internally
consistent with anything landed above, check this file first — as of this
writing, all four still substantially describe the pre-integration shape.
