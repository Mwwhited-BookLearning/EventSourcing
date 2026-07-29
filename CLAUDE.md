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
- `docs/comparisons/` — a **fourth kind of document**: when a real fork
  has genuine options on more than one side, write both (or all) sides
  out in full — pros, cons — *before* the deciding ADR, not after. Write
  one of these whenever a multi-option decision comes up, then have the
  ADR reference it rather than re-deriving the comparison inline.
- `docs/patterns/interactions/` — for when two patterns don't just both
  apply somewhere, but genuinely *compose* at one specific point (e.g.
  two different checks running in the same fold step) — gets its own page
  explaining the combination, linked from both patterns' own docs.
- `docs/design-docs/` — a **second, independently-developed design**
  (a distributed, entity-centric event-sourced platform) that has now been
  fully absorbed into ADRs `021`–`039` (see "Integration status" below).
  Kept for provenance/narrative context, not because anything still only
  exists there — every decision it raised has a real ADR now.

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

## Standing requirements, now attached to a written ADR

- **Fault/abend/restart-tolerant outbox** — durable and resumable across
  an unclean process termination, not merely safe under a graceful
  shutdown. Addressed in `ADR-033` (peer-sync outbox/inbox) and referenced
  by `ADR-039` (the MVVM client's local outbox reuses the same durable
  primitive). If a *third* outbox-shaped mechanism ever gets introduced,
  re-check that it actually inherits this, don't assume it does by family
  resemblance alone.
- **Never lose or corrupt data** — the governing principle stated in
  `README.md`'s opening section. Weigh any future durability trade-off
  against this explicitly (see `ADR-031`'s streaming-channel durability
  bar for the one accepted, narrow exception, stated as such rather than
  a silent default) rather than defaulting toward convenience.

## Integration status (`docs/design-docs/` → primary design)

**Every ADR the design-docs merge implied now exists and is Accepted —
`ADR-021` through `ADR-039`.** Nothing from that merge remains only a
plan; what remains is propagating those decisions into the surrounding
docs that still describe the pre-integration shape (see below). Big,
foundational, locked-in decisions, for quick orientation:

- **Full entity-centric rebuild** — `EntityId`, an always-on Entity Store,
  `ExpectedVersion` (`ADR-021`), refined by `Optional<T>` property-level
  patches (`ADR-022`), a persist-everything ingestion posture (`ADR-023`,
  superseding parts of `ADR-011`/`013`/`020`), and optimistic concurrency
  + logical-order fold (`ADR-024`, `ADR-029` — see
  `docs/patterns/interactions/fold-ordering-and-conflict.md` for how the
  two compose).
- **Multi-tenant framework** — `appId` is a real schema-registry scoping
  key (`ADR-030`); the `Orders` walkthrough is a sample application, not
  part of the core engine.
- **Schema evolution went further than design-docs asked for** —
  upcasting is materialized to the log, not just computed live
  (`ADR-018`, `ADR-027`), downcasting exists for legacy consumers
  (`ADR-028`), and compatibility/deployment discipline is explicit
  (`ADR-038`).
- **Two new data planes, deliberately separate from the event log** —
  streaming channels for telemetry/audio/video (`ADR-031`, with
  standard-protocol playback/deep-linking/redaction) and content-addressed
  binary attachments, browsable via WebDAV (`ADR-032`).
- **Distribution is real** — gossip-topology replication with a minimum
  2-replica, regional-fault-tolerant requirement (`ADR-033`,
  `docs/comparisons/peer-sync-topology.md`) and entity-type-based sharding
  (`ADR-034`, `docs/comparisons/sharding-strategy.md`).
- **Non-authoritative capture** — `AuthorityStatus` as its own trust axis
  (`ADR-035`, `docs/comparisons/authority-rejection-behavior.md`), DID/UCAN
  self-attestation via OAuth Token Exchange (`ADR-036`, un-rejecting what
  `references.md` had marked reference-only).
- **GraphQL replaces OData entirely** (`ADR-037`) — not "primary/
  secondary." GraphQL queries travel over HTTP `QUERY`, never `GET`,
  specifically to keep PII/PHI-bearing filter arguments out of URLs/
  logs/proxy caches. Supersedes `ADR-003`/`04-odata-filter-pushdown.md`'s
  OData surface (the per-provider JSON pushdown mechanism survives; only
  the OData syntax goes away) and moves `ADR-018`'s upcast mechanism off
  OData `compute()` onto JS/CEL + GraphQL SDL directives.
- **MVVM client + entity views** (`ADR-039`) — sequenced last on purpose,
  the least load-bearing piece; composes primitives every earlier ADR
  already built rather than introducing new server-side mechanism.

Also landed, independent of the entity/persist-everything direction
(tooling/infrastructure, decided alongside the merge but not part of it):
`ADR-025` (Scalar for OpenAPI docs UI, `@asyncapi/react-component` for
AsyncAPI) · `ADR-026` (dev via .NET Aspire + full OpenTelemetry — logging,
tracing, metrics; prod via Docker Compose).

### What's actually still outstanding: propagation, not decisions

- **Done and propagated:** `ADR-021`–`024` fully reflected in `docs/data/`
  and `03-api-contracts.md`. `ADR-025`/`026` propagated into
  `06-solution-structure.md` (`ServiceDefaults`' `ConfigureOpenTelemetry`
  cross-reference, `/scalar` and `/asyncapi-ui` route mapping) and
  `references.md`. `ADR-031`/`032`'s new entities are in
  `docs/data/streaming-and-attachments.md`.
- **Not yet propagated anywhere beyond `docs/data/`'s field-level
  additions and the ADR index**: `ADR-027`–`030`, `ADR-033`–`039`. In
  particular:
  - `01-c4-architecture.md` doesn't yet show the Entity Store, streaming/
    attachment containers, replication topology, sharding, or a GraphQL
    Gateway (still shows the old OData-era Follow/Lineage containers).
  - `06-solution-structure.md` doesn't yet reflect the multi-tenant
    (`AppId`-keyed) registry lookups, the peer-sync outbox/inbox project,
    the GraphQL resolver layer, or the MVVM client project structure.
  - `08-build-plan.md` has no phases yet for replication, sharding,
    non-authoritative capture, the GraphQL swap, streaming channels,
    attachments, or the MVVM client — still ends at the original
    CQRS-projections phase.
  - `03-api-contracts.md` still documents the OData-era
    `$filter`/`QUERY` contracts `ADR-037` supersedes.
  - `04-odata-filter-pushdown.md` needs a superseded banner (or a rename)
    per `ADR-037` — not yet touched.
  - `features/*.md` Gherkin scenarios still assert the pre-`ADR-023`
    reject-on-invalid behavior (`400` instead of `202`+`SchemaStatus`)
    and don't cover any capability past `ADR-024`.

Before assuming any of those five files are internally consistent with
`ADR-025` onward, check this section first.
