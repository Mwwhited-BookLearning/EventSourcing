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
  structure → ADRs → build plan → CQRS read side).
- `docs/07-adrs.md` — **template + index only.** The ADRs themselves live
  one per file under `docs/adrs/adr-NNN-slug.md`. Never write ADR content
  back into `07-adrs.md` — add a row to its index table and create the file
  under `adrs/`.
- `docs/features/*.md` — one doc per feature: context, PlantUML
  sequence/ER diagrams, Gherkin scenarios. Extracted into real `.feature`
  files only once implementation starts (see `06-solution-structure.md`).
- `docs/references.md` — bibliography. Two sections: standards actually
  **adopted** (with which ADR/doc uses them), and standards **considered
  and rejected**, each with the specific reason. Every new ADR that leans on
  a real-world spec/library should get an entry here, and every rejection
  should be as explicit as an adoption — don't let something get evaluated
  and then silently disappear.
- `docs/design-docs/` — a **second, independently-developed design**
  (a distributed, entity-centric event-sourced platform) that is currently
  being merged into the primary design above. See "Integration status"
  below before assuming anything in `01`–`09`/`adrs/` reflects the final
  state — several foundational pieces have already changed.

## Conventions established so far

- **ADRs are additive history, not editable state.** When a later decision
  changes an earlier one, don't delete the old text — strike it through
  (`~~...~~`) and add "Superseded by `ADR-XXX`" (see `ADR-006`'s
  `access_token` workaround for the pattern). If the earlier ADR was written
  *this same integration effort* and never shipped/built, a clean rewrite in
  place is fine instead (see `ADR-018`'s upcast mechanism, revised in place
  before anything depended on the original version).
- **Verify a spec before citing it.** Every RFC/standard number cited in an
  ADR was confirmed against the real spec (WebFetch the datatracker/spec
  page) before being written down, not recalled from memory and assumed
  correct. Do the same for any new citation — this bit once already (an
  RFC number needed correcting via direct lookup).
- **Never invent a bespoke mechanism when a real standard already fits.**
  This project's default move when a design question comes up is: check
  whether an existing RFC/spec/library already solves it, adopt it if the
  fit is genuine, and explicitly record in `references.md` why *not* if it
  was considered and rejected. Don't reach for a hand-rolled DSL/format
  first.
- **Say when something is only partially borrowed.** Several ADRs
  deliberately borrow a standard's syntax without claiming full spec
  compliance (e.g. OData-flavored `$filter`/`$top`/`$skip`). State that
  explicitly in the ADR rather than letting a reader assume full compliance.
- **Disambiguate terminology collisions explicitly**, don't just hope
  context makes it clear. Two examples already handled this way: "query
  parameter" vs. the HTTP `QUERY` method (`ADR-010`'s note), and
  "projection" meaning a CQRS read model (`ADR-015`/`016`) vs. the schema-
  mapping sense the word has in `docs/design-docs/07` (`ADR-018`'s note).
- **Feature docs use a one-line disclaimer instead of rewriting every
  scenario** when a mechanical, project-wide change (like the `GET`→`QUERY`
  method swap) would otherwise require touching dozens of Gherkin lines
  purely for notation, not substance.
- **A new capability gets a Phase in `08-build-plan.md`**, with real
  dependencies on the phases it needs and concrete exit criteria tied to a
  feature doc's Gherkin scenarios — not just an ADR with no build-plan
  entry.

## Integration status (`docs/design-docs/` → primary design)

In progress, sequenced as ADRs `021` onward. Big, foundational, locked-in
decisions already made for this merge (don't re-litigate without a strong
reason — these came from explicit direction, not a default guess):

- **Full entity-centric rebuild** — `EntityId`, an always-on Entity Store,
  `ExpectedVersion` are now real (`ADR-021`), not the lighter "keep
  EventSouring's model as-is" option.
- **Persist-everything ingestion** — publish returns `202` + a status
  envelope; schema/authority problems become advisory flags, not `400`s
  (`ADR-023`). This is a genuine reversal of `ADR-011`/`013`/`020`'s
  original reject-on-invalid framing — expect those three to keep needing
  small consistency fixes as more of the merge lands.
- **Distribution is in scope** — sharding + multi-origin replication are
  being built out (`ADR-027`/`028`), not deferred the way `ADR-007` is.
- **GraphQL replaces OData entirely** — not "GraphQL primary, OData
  secondary" (design-docs' own original recommendation), a full swap.
  GraphQL queries travel over the HTTP `QUERY` method specifically (not
  `GET`) to keep filter arguments — which may carry PII/PHI — out of URLs,
  access logs, and proxy caches. This supersedes `ADR-003` and
  `04-odata-filter-pushdown.md`'s OData surface (the underlying per-
  provider JSON pushdown mechanism survives; only the OData syntax on top
  of it goes away) and moves `ADR-018`'s upcast mechanism off OData
  `compute()` (which justified itself by reusing the OData parser — gone
  now) onto JS/CEL transforms + GraphQL SDL directives instead.

Two more ADRs landed alongside this merge but are independent of it —
tooling/infrastructure decisions, not part of the entity/persist-everything
direction: **`ADR-025`** (Scalar for the OpenAPI docs UI,
`@asyncapi/react-component` for the AsyncAPI one) and **`ADR-026`**
(local dev via .NET Aspire with all three OpenTelemetry signals —
logging, tracing, metrics — wired through `ServiceDefaults`; production
via Docker Compose, elevated from `ADR-006`'s original "fallback" framing
to the actual production path).

**Done and propagated:** `ADR-021`–`026` — entity concept, `Optional<T>`
property-level patches refining `ADR-016`, persist-everything posture,
optimistic concurrency/conflict flagging (reflected in `02-data-model.md`
and `03-api-contracts.md`), API documentation UI, and dev/prod deployment
split — the last two still need their propagation pass into
`06-solution-structure.md` (`ServiceDefaults`' OTel wiring, the
`Scalar`/`asyncapi-ui` routes) and `references.md`.

**Queued, not yet written — renumbered to start at `027`** (the design-docs
merge ADRs originally planned as `025`–`031` shifted up by two once
`ADR-025`/`026` above claimed those numbers — ADR numbers are assigned by
write order, never reused, per the convention above): `ADR-027`
(replication) · `ADR-028` (sharding) · `ADR-029` (non-authoritative
capture/`AuthorityStatus`) · `ADR-030` (DID/UCAN + OAuth Token Exchange
RFC 8693 — un-rejecting what `references.md` had marked reference-only,
now that `ADR-029` creates the need it was missing) · `ADR-031`
(GraphQL-only query layer, see above) · `ADR-032` (compatibility/
deployment discipline — Tolerant Reader, Expand/Contract migrations,
N-1/N+1 window) · `ADR-033` (MVVM client + HTML/JS entity view
definitions — the least load-bearing piece for everything else; fine to
sequence last or pause on). Plus a full propagation pass into
`01-c4-architecture.md`, `06-solution-structure.md`, `08-build-plan.md`,
and `references.md` once those land.

Before assuming any of `01-c4-architecture.md`, `06-solution-structure.md`,
`08-build-plan.md`, or `references.md` are internally consistent with the
new entity/persist-everything/GraphQL direction, check whether the queued
items above have landed yet — as of this writing they haven't, so those
four files still describe the pre-integration (OData, reject-on-invalid,
single-store) shape in places.
