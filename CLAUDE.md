# CLAUDE.md

This repo started as a **design package** and, this session, began actual
implementation (direct request: "start converting the build plan to your
active TODO... let's do this"). `src/`/`tests/` now exist, built strictly in
`docs/08-build-plan.md`'s dependency order — that file's "Implementation
status" table (near its top) is the authoritative tracker of which item is
done/in progress/not started; check it before assuming any capability
exists in code. Every file under `docs/` is still the architecture/decision
documentation the implementation is built *from* — when code and docs
disagree, a doc is wrong and gets fixed (the data-model docs remain the
shape authority per the standing rule below), not silently overridden by
whatever the code happened to do. Treat doc edits with the same care as
code edits: internal consistency across files matters more here than
almost anywhere else, because a design decision left inconsistent
propagates into real code, not just prose.

Note: the folder/repo name itself is a typo (`EventSouring` → should be
`EventSourcing`). Known, deliberately not yet fixed — renaming the directory
this session's tools are actively running against was judged too risky
mid-task. Confirm with the user before doing it, and do it as an isolated
step (rename, then verify the shell's cwd survived) — don't fold it into a
larger batch of unrelated changes.

**This file is an index and a standing-conventions reference, not a
changelog.** It used to also narrate the full reasoning behind every
decision made this project — that content is now redundant with the
decisions themselves (every ADR, comparison, domain doc, and glossary
entry already explains its own "why") and was moved out, this session,
per direct request, to keep this file from growing unboundedly every
pass. **Daily change logs now live in `docs/changes/{date}.md`** —
`docs/changes/2026-07-30.md` has the full narrative this file used to
carry. Add a new dated file for future sessions' work rather than
growing this file again; keep this file itself to conventions/index
material only.

## Reusable process material

**`.claude/templates/`** and **`.claude/protocols/`** (this session) hold
the actual step-by-step conventions for doing recurring work
consistently — read these before starting the matching task, don't
re-derive the process from scratch:
- `templates/adr-template.md`, `feature-doc-template.md`,
  `domain-doc-template.md`, `comparison-doc-template.md` — the exact
  skeleton for each doc kind below.
- `protocols/verify-before-citing.md` — the research-before-design rule
  (this project's single most repeated standing instruction).
- `protocols/additive-history-editing.md` — how to revise an already-
  Accepted ADR without losing history.
- `protocols/parallel-batch-dispatch.md` — how to split large multi-file
  work (one unit per domain/ADR-range/review-lens) across parallel
  background agents without them racing on shared files.
- `protocols/todo-tracking.md` — how to add/complete an item in
  `TODO.md` and how that differs from `docs/10-open-questions.md` (a
  fork not yet decided) and `docs/changes/{date}.md` (history of work
  already done).
- `protocols/context-handoff.md` — how to keep `.claude/context.md`
  current: a session-handoff snapshot, not a log, so a fresh session
  can resume from the repo alone.

## Layout

- `README.md` — entry point: what the system is, doc index, open decisions.
- `.claude/context.md` — **read this first when resuming cold** (a new
  session, a lost/unresumable conversation, a handoff to someone else):
  a snapshot of current state, what's actively in flight, and working
  notes that only ever existed in conversation until captured here. Kept
  current per `.claude/protocols/context-handoff.md` — a dashboard to
  overwrite each session, not a log to append to.
- `TODO.md` — the live tracker for concrete, already-decided work not yet
  done (a doc rewrite, a missing diagram, a rename) — distinct from
  `docs/10-open-questions.md`'s undecided forks and from
  `docs/changes/{date}.md`'s history of work already finished. See
  `.claude/protocols/todo-tracking.md` for the add/complete workflow.
- `docs/01`–`09` — the core design, read in that order (C4 architecture →
  data model → API contracts → GraphQL filter pushdown → schema registry
  → solution structure → ADRs → build plan → CQRS read side). `04-odata-
  filter-pushdown.md`'s filename is unchanged (stable numbering) but its
  content was rewritten, this session, to describe the current GraphQL-
  driven mechanism — the OData-era pipeline is preserved as a clearly
  marked historical section within it, not deleted, per this file's own
  additive-history convention.
- `docs/02-data-model.md` — **classification overview + index only**, same
  split as the two below. The entity classes themselves live one group per
  file under `docs/data/` (`schema-registry.md`, `event-log.md`,
  `entity-store.md`, `dbcontext-and-conventions.md`, `streaming-and-
  attachments.md`, `access-log.md`). Never write an entity class back into
  `02-data-model.md` — add it to the right group file and update the
  classification table/diagram if it's a new group.
- `docs/10-open-questions.md` — the live tracker for genuinely unresolved
  questions, distinct from every other document type: an ADR is a
  decision already made, a comparison weighs a fork before deciding,
  this file is for a fork not yet weighed at all or a decision left
  deliberately partial. **Add a row here in the same pass you find one.**
  Once resolved, **delete the row outright** — the resolving ADR is
  already its permanent record; that day's `docs/changes/{date}.md` gets
  a one-line pointer instead of a retained, struck-through copy. If
  another doc cites a row by number, update that citation to point at
  the resolving ADR/changelog when the row is deleted, rather than
  leaving a dangling reference.
  **This is the authoritative current list of open forks — do not
  restate its contents elsewhere in this repo, including here in
  CLAUDE.md; a restated count/list will just drift out of date.**
- `docs/07-adrs.md` — **template + index only.** The ADRs themselves live
  one per file under `docs/adrs/adr-NNN-slug.md`, all currently Accepted
  through `ADR-094`. Never write ADR content back into `07-adrs.md` — add
  a row to its index table (it already carries a one-line description per
  ADR — that index, not this file, is where to look for "what does ADR-NNN
  decide") and create the file under `adrs/`.
- `docs/patterns/` — a **pattern reference**, distinct from an ADR (a
  specific decision) and from `references.md` (a bare bibliography line):
  explains the general pattern first (cited), then points to the ADR(s)
  that apply it here. **Keep this folder in sync as new patterns/practices
  get discovered or decided — a standing instruction, not a one-time
  task.** At minimum, add a row to `patterns/README.md`'s catalog the same
  turn a new pattern gets decided; write the full standalone doc (with a
  PlantUML/Salt diagram, whichever fits) when there's time to do it
  properly. This folder has fallen behind twice already and been caught
  by a later review both times — don't let it happen a third time.
- `docs/features/*.md` — one doc per core-engine feature: context, PlantUML
  sequence/ER diagrams, a Salt mockup (or an explicit "not applicable, see
  X" if there's no UI surface), and embedded Gherkin scenarios. Extracted
  into real `.feature` files only once implementation starts (see
  `06-solution-structure.md`). Use `.claude/templates/feature-doc-
  template.md`.
- `docs/references.md` — bibliography. Two sections: standards actually
  **adopted** (with which ADR/doc uses them), and standards **considered
  and rejected**, each with the specific reason. Every new ADR that leans on
  a real-world spec/library should get an entry here, and every rejection
  should be as explicit as an adoption. **A rejection can later flip to
  adopted** if a new requirement removes the original reason for rejecting
  it — this has happened repeatedly (content-addressable storage,
  SPIFFE/SPIRE, YARP, WebDAV's `nwebdav.md` going the *other* direction
  after initially being adopted) — check for a stale rejection (or a stale
  adoption) before assuming the current framing is still right.
- `docs/comparisons/` — a **fourth kind of document**: when a real fork
  has genuine options on more than one side, write both (or all) sides
  out in full — pros, cons — *before* the deciding ADR, not after. Use
  `.claude/templates/comparison-doc-template.md`.
- `docs/patterns/interactions/` — for when two patterns don't just both
  apply somewhere, but genuinely *compose* at one specific point — gets
  its own page explaining the combination, linked from both patterns'
  own docs.
- `docs/libraries/{platform}/{library}.md` — a **fifth kind of document**:
  one file per adopted (or seriously considered) off-the-shelf
  framework/library, grouped by platform folder (`dotnet/`, `web/`, ...).
  What it's for, plus general usage examples. Referenced from whichever
  ADR/pattern doc actually adopts it. See `docs/libraries/README.md` for
  the catalog and the buy-over-build principle this folder exists to
  support — it also doubles as this project's IEC 62304 SOUP list
  (`ADR-074`).
- `docs/domains/{domain}/` — a **sixth kind of document**, restructured
  into subfolders this session: `README.md` (applicable ADRs, governing
  regulations/standards, special concerns, a `## Glossary` of that
  industry's own jargon — use `.claude/templates/domain-doc-template.md`)
  plus `features/*.md` (entity/event structures, a state-machine
  workflow diagram, a Salt mockup, embedded Gherkin — one real use case
  worked through end-to-end, using `.claude/templates/feature-doc-
  template.md`, the same depth as `docs/features/*.md` for the core
  engine). **The 13 considered-not-chosen domains stay at one feature
  doc each; the two chosen proving-ground domains (clinical trials +
  device telemetry, digital identity/KYC) were taken further, sequenced
  into a `## Workflows` section on that domain's own `README.md`** — see
  `docs/changes/2026-07-30.md`'s "two chosen domains taken to full
  reference-application depth" section. Originally 4 feature docs/3
  workflows each; clinical trials + device telemetry grew to 5 feature
  docs/4 workflows on direct request once `ADR-094` gave its named IONM
  use case a real mechanism to exercise end-to-end
  (`docs/changes/2026-08-04.md`) — the two domains were never required to
  stay at matching depth, that was just how it happened to work out
  until this addition. Distinct
  from `docs/glossary.md`, which covers Duplex's own
  cross-cutting engine terms once, not per domain. Generated from — not
  a repeat of — `docs/comparisons/proving-ground-domain.md`'s coverage
  matrix and regulatory mapping table. `docs/domains/README.md` is the
  catalog.
- `docs/naming.md` — company/product naming (OoBDev; `Duplex` the base
  engine; `Vitals`/`Meridian` the two proving-ground products). Not an
  architecture decision, kept separate on purpose.
- `docs/glossary.md` — every cross-cutting Duplex engine term, defined
  once, cross-referenced to its deciding ADR, with verified synonyms
  where real ones exist.
- `docs/design-docs/` — **removed.** Was a second, independently-developed
  design, imported purely as a reference for the `ADR-021`–`039`
  integration, fully absorbed, then deleted once absorption was confirmed
  complete. A `docs/design-docs/NN §X.Y`-style citation surviving in an
  ADR/pattern/comparison doc is a provenance pointer to that now-deleted
  source — leave those as historical attribution, don't add new ones.

## Conventions established so far

- **No external `!include` in any PlantUML diagram, ever — hand-style C4
  notation in plain PlantUML instead.** `C4-PlantUML` fails silently (a
  blank or broken diagram, no readable error) in any renderer without
  live internet access or that exact stdlib path configured, which is
  most local/offline setups actually used against this repo — happened
  repeatedly, not once. See `references.md`'s `C4-PlantUML` reference-
  only entry for the full reasoning. Applies to every PlantUML diagram in
  this repo, not just C4 ones.
- **ADRs are additive history, not editable state.** See
  `.claude/protocols/additive-history-editing.md` for the full rule.
  This has mattered concretely: `ADR-006`'s `access_token` workaround,
  `ADR-057`'s reversal of `ADR-009`'s no-erasure stance, and a design
  review this session catching three ADRs (`018`, `020`, `046`/`047`)
  that had silently drifted from a later decision with no marker at all.
- **Never hardcode a future ADR's number in a propagated doc.** Write
  "the queued X ADR," not "`ADR-027`," for anything not yet actually
  written. ADR numbers are assigned by write order and this has been
  violated and had to be fixed multiple times this session — don't add
  more. Backfill the real number only once that ADR exists.
- **Search for prior art before designing anything new, and verify
  every citation before writing it down.** See
  `.claude/protocols/verify-before-citing.md` for the full rule and why
  it matters — this is this project's single most repeated standing
  instruction.
- **Never invent a bespoke mechanism when a real standard already
  fits; prefer buy over build for libraries the same way.** Record the
  reason explicitly in `references.md` either way (adopted, or
  considered-and-rejected) — don't let an evaluation silently
  disappear.
- **Say when something is only partially borrowed**, and disambiguate
  terminology collisions explicitly rather than hoping context makes it
  clear (examples: "query parameter" vs. the HTTP `QUERY` method,
  `ADR-010`; "projection" as a CQRS read model vs. design-docs' schema-
  mapping sense, `ADR-018`; `ChannelOrigin.Origin` vs. `OriginId`,
  disambiguated inline in `docs/data/streaming-and-attachments.md`).
- **The ADR that adds or changes a persisted field/entity/table is that
  field's naming/shape authority — and must land the matching
  `docs/data/*.md` edit and `DbSet` registration in the *same pass*, not
  defer it to a later sweep.** A recurring drift table in `TODO.md`
  (`OriginId`/`LogicalClock` described in `ADR-033` but never added to
  `docs/data/event-log.md`; `RequiredClaims` singular vs. `ADR-050`'s
  list; missing `DeprecatedAt`/`ViewDefinition`/`PeerSyncCursor`/
  `WebhookOutbox`; seven entities with no `DbSet`) happened because this
  step got skipped repeatedly, not because anyone disagreed about the
  field — the ADR's prose was never actually wrong. This bullet is the
  rule that stops it recurring. A full docs-vs-implementation audit
  (this session) closed every item that drift table named — see
  `docs/changes/2026-08-11.md` — so the table itself is gone from
  `TODO.md` now; if a new instance of this class of drift ever surfaces
  again, it goes back in `TODO.md`, not here.
- **A repeated relationship gets its own envelope-metadata field, never
  conflated with an existing one just because the shape looks similar.**
  This design has eight now: `parentEventIds` (causal derivation,
  `ADR-005`), `MaterializationOfEventId` (reshaped copy of, `ADR-027`),
  `TelemetryPointer` (position in a signal/media stream, `ADR-031`),
  `AttachmentRef` (supporting binary content, `ADR-032`), `erasureScope`
  (whose crypto-shredding key protects this field, `ADR-057`),
  `Signature` (a captured sign-off attestation, `ADR-066`),
  `OriginalSequenceNumber`/`OriginalChainHash`/`ImportedFrom` (provenance
  of an imported lineage-export event, `ADR-068` — added without the
  explicit "ask before a seventh" gut-check this convention calls for;
  flagged, not undone, since the fit is genuine on inspection), and
  `RespondsToEventId` (which prior event this one satisfies a declared
  response expectation for — the Correlation Identifier pattern, Hohpe &
  Woolf — distinct from `parentEventIds`' broader, untimed causal
  derivation; `ADR-094`, gut-check done explicitly this time). If a
  ninth comes up, ask what question it specifically answers first.
- **A new capability gets a named item in `08-build-plan.md`.** That file
  moved off fixed `Phase N` numbering this session — each item names its
  own prerequisite items instead of a phase number, so adding one never
  requires renumbering anything else. Cite an item by name
  (`` `08-build-plan.md`, "Event-Type Security" ``), never by a number.
- **`08-build-plan.md`'s single dependency-order PlantUML diagram
  (`BuildPlan_All` — consolidated this session from two separate diagrams,
  `BuildPlan_CorePhases`/`BuildPlan_Additions`, per direct request) tracks
  build status by fill color, kept in lockstep with the "Implementation
  status" table's own `Status` column, in the same pass, every item, not
  just at the end of a session:** no fill = `Not started`,
  `#palegoldenrod` = `In progress` (set the moment work starts on that
  item), `#palegreen` = `Done`. This is now itself a standing implementation-tracking
  requirement, not a one-time diagram edit — update both the table row
  and that item's `state` line's fill together whenever its status
  changes.

## Standing requirements, now attached to a written ADR

- **Fault/abend/restart-tolerant outbox** — durable and resumable across
  an unclean process termination. Addressed in `ADR-033` (peer-sync
  outbox/inbox), referenced by `ADR-039` (client outbox) and `ADR-060`
  (webhook dispatcher). If a *fourth* outbox-shaped mechanism ever gets
  introduced, re-check it actually inherits this, don't assume it does
  by family resemblance alone.
- **Never lose or corrupt data** — the governing principle stated in
  `README.md`'s opening section. Weigh any future durability trade-off
  against this explicitly (see `ADR-031`'s streaming-channel durability
  bar for the one accepted, narrow, named exception) rather than
  defaulting toward convenience.

## Decision index

All 95 ADRs (`ADR-001`–`ADR-095`) are Accepted. For what each one
decides, read `docs/07-adrs.md`'s index (one-line description per row)
or the ADR file itself — not this section, which no longer narrates
individual decisions. For the forks weighed before a decision, see
`docs/comparisons/README.md`. For the two chosen proving-ground domains
(and 13 considered-not-chosen) and how this framework's mechanisms map
onto each, see `docs/domains/README.md`. For terminology, `docs/
glossary.md` (engine-wide) and each domain's own `README.md#glossary`
(industry-specific).

Two structural notes worth keeping here, since they're easy to get
backwards when reading an individual ADR in isolation:
- **`ADR-021`–`ADR-039`** is the entity-centric rebuild that everything
  else in this design assumes (`EntityId`, the always-on Entity Store,
  persist-everything ingestion, GraphQL replacing OData entirely, and
  the MVVM client) — read these first if you're new to the design.
- **`ADR-075` revises `ADR-030`**: tenant isolation is now the silo
  model (one dedicated deployment per tenant), not the pool model
  (`AppId` scoping inside one shared deployment) `ADR-030` originally
  assumed. `AppId` itself is unaffected — it now scopes applications
  *within* one tenant's own deployment. `ADR-033`/`034`'s mechanisms are
  unchanged, now understood to operate within one tenant's deployment,
  never across different tenants'. See `docs/comparisons/multi-tenant-
  isolation-model.md` for the full reasoning.

### Propagation status

The concrete list of what's not yet propagated/fixed lives in
[`TODO.md`](TODO.md), not here — restating it in both places would just
drift stale, the same reasoning `docs/10-open-questions.md` already
applies to itself. Check `TODO.md` before assuming any doc is fully
consistent with the latest ADRs.

What's already been checked and resolved — a full 74-ADR compliance
review, a fresh-eyes contradiction hunt across all 75, a buildability
check, and a mechanical cross-reference sweep, all this session — is
narrated in `docs/changes/2026-07-30.md`, including the real bugs those
passes found and fixed (`ADR-032`'s WebDAV mounting described as shipped
when it wasn't, `ADR-046`/`047`/`067`'s `Role`/`UserPermission` ownership
disagreement, `ADR-018` never actually revised off OData, `ADR-068`'s
overclaimed offline hash re-verification). Not repeated here either.
