# Project Context (session handoff)

**This is a snapshot, not a log — overwrite it in place each session,
don't append to it.** History lives in `docs/changes/{date}.md`; open
forks live in `docs/10-open-questions.md`; active tasks live in
`TODO.md`. This file exists so a fresh agent (or a human) can resume
from the repo alone, without replaying git-log archaeology or losing
information the way an earlier, unresumable conversation did. See
`.claude/protocols/context-handoff.md` for the update rules.

## What this project is

`EventSourcing` (repo name is a known typo for `EventSourcing` — see
`CLAUDE.md`, deliberately not yet renamed) is a **design package, not an
implemented codebase** — there is no `src/` yet, only `docs/`. It's a
from-scratch design for an event-sourcing store ("Duplex," `docs/
naming.md`), built as a **worked teaching example**: append-only write
side (schema registry, publish/follow/lineage APIs), a CQRS read side,
and two fully worked proving-ground domains (clinical trials + device
telemetry — "Vitals"; digital identity/KYC — "Meridian"). Governing
principle: never lose or corrupt data. All 93 ADRs (`docs/adrs/adr-001`
through `adr-093`) are Accepted — the *decisions* are essentially done;
what's left is propagating them consistently across ~150 files with no
compiler to catch drift, which is why internal consistency matters more
here than in most repos.

## Current state

*(update this section's content, not just its presence, every session —
stale numbers here are worse than none)*

- As of **2026-07-31**: HEAD is `a602317` ("updating design") — moved
  several times this session via commits not made by this conversation
  (an external process/terminal checkpointing the work; harmless, just
  don't assume HEAD is `0bd157d` anymore). Working tree still has
  unstaged edits from this session's late review pass — see `git status`.
- **`docs/10-open-questions.md` is now EMPTY** — worked down from 22
  rows to 0 this session, via twenty-two new ADRs (`ADR-076`–`093`) plus
  an `ADR-045` addendum. Full narrative in `docs/changes/2026-07-31.md`,
  not repeated here. Rows 3/4 relocated (not resolved) to their owning
  domains' `README.md`s; row 7 fully excluded (pure ops, not a
  framework fork at all — a new standing exclusion category in the
  file's own header now). **This is a milestone, not a steady state** —
  the file's whole purpose is to catch the *next* real fork the moment
  it's found, not to stay empty. Standing policy, reversed once this
  session (don't re-derive either version from scratch — see the file's
  own header): a resolved row is **deleted outright**, not struck
  through; the resolving ADR is its permanent record, and that day's
  `docs/changes/{date}.md` carries the one-line pointer instead.
  Recurring lenses worth applying to whatever's found next: **(1)** an
  ops/runbook concern with no real architecture/dev decision embedded
  can be excluded entirely, not just deferred; **(2)** prefer a real
  RFC/web standard over a custom mechanism when one's available;
  **(3)** reuse an existing framework mechanism/interface before
  inventing a new one.
- **A full review pass over all 22 new ADRs, this session, found and
  fixed 8 real issues** (4 parallel review agents, each checking a
  different axis — citation accuracy, data-model-propagation-claims-vs-
  reality, index/tracker consistency, and references/domain-doc
  consistency): `ADR-086` misnamed a hash ("root" → the correct
  "manifest hash," matching `ADR-068`'s own term); `ADR-092`
  mischaracterized both `ADR-035`'s actual scope (it's non-authoritative
  capture of an *authenticated* claim, not "unauthenticated," since
  `ADR-042` revised the default) and `ADR-058`'s (which already says
  "hostile," contradicting the claim that it doesn't); `ADR-088`'s fold-
  lag metric didn't account for `ADR-042`'s review-gating (would have
  conflated processing latency with open-ended human-review time);
  `ADR-077`/`ADR-078` claimed data-model propagation as "not yet done"
  when it had actually already landed in the same session; a typo/count
  error in `TODO.md` and a stale `75→85` (should be `93`) count in
  `docs/changes/2026-07-31.md`; and `docs/domains/digital-identity-kyc/
  README.md` had a real partial-edit bug — only its Special Concerns
  bullet was updated to reflect `ADR-079`'s resolution, while five other
  spots in the same file (the regulations table, a workflow description,
  the feature-docs list, two glossary entries) still called the OFAC/SAR
  question open. All fixed. **Lesson for next time**: verify propagation
  claims against the actual files rather than trusting an ADR's own
  Consequences section, and grep a whole file for a topic before
  assuming one updated bullet means the whole file is consistent.
- **`TODO.md` restructured into 6 dependency-ordered phases** this
  session (was a flat 12-item list) — Phase 1 quick/independent fixes,
  Phase 2 data-model correctness (foundational), Phase 3 diagrams/
  library catalog, Phase 4 the GraphQL/API-contract rewrite cluster
  (internally sequenced: pushdown doc → api-contracts → solution-
  structure → Gherkin scenarios), Phase 5 the `08-build-plan.md`
  dependency-checklist restructuring, Phase 6 the 13-domain Salt-mockup
  rework (sized for parallel dispatch). Read `TODO.md` directly rather
  than this summary for the actual items.
- Also created, this session: `TODO.md`'s restructure aside, `.claude/
  protocols/todo-tracking.md` + `context-handoff.md` (both created
  earlier the same session).
- **Still open, not yet executed**: everything in `TODO.md`'s 6 phases,
  plus a full repo-wide staleness review pass beyond what this
  session's ADR-focused review covered — see "Working notes" below for
  retained detail on anything not fully captured by `TODO.md` itself.

## How to resume cold

1. Read `CLAUDE.md` (standing conventions + doc-type index).
2. Read this file, then `TODO.md` (active work) and
   `docs/10-open-questions.md` (open design forks).
3. `git log --oneline -10` and `git status` — confirm this file's
   "Current state" section still matches reality; if it doesn't,
   something changed without this file being updated (fix that first).
4. Skim the latest `docs/changes/{date}.md` for the most recent
   session's narrative.

## Working notes not yet written down elsewhere

- The user explicitly wants to be asked before large, effort-heavy
  content rewrites get started unilaterally (e.g. the 13-domain Salt-
  mockup rework) — offer it, don't just do it. Smaller, unambiguous
  fixes (broken links, typos, missing cross-references) are fine to fix
  directly during a review pass.
- The two-tier domain depth (13 domains at 1 feature doc each vs. 2
  chosen domains at 4 feature docs / 3 workflows each) is intentional,
  not unfinished — don't mistake the 13 shallower domains for a batch
  that still needs finishing to 4 docs each.
- **`08-build-plan.md` restructuring — agreed direction, not yet
  executed.** Replace fixed `Phase N` labels with a dependency-checklist
  model (each item declares its own prerequisite ADRs/items; display
  order/grouping is derived via topological sort, not hand-assigned) so
  adding a new capability never again requires renumbering or risks
  being skipped the way `ADR-050`–`079` were. The existing PlantUML
  dependency graph already models the right relationships through
  ~`ADR-048`; this reframes it as data instead of hand-maintained edges,
  then backfills the missing ADRs.
- **The missing GraphQL-pushdown doc** (replacing `04-odata-filter-
  pushdown.md`) — agreed direction, not yet written. Confirmed as a
  **query-pattern** doc, not a projection or CEL-based one. Filtering
  follows the Query pattern (GraphQL `Query` → HotChocolate
  `[UseFiltering]` → `IQueryable<Entity>` → same `IJsonPathTranslator`
  pushdown `ADR-037` already says survives). Projection (`[UseProjection]`,
  field-shaping) is a separate, mostly-free GraphQL bonus, not what this
  doc replaces. CEL stays scoped to upcast mapping only — reusing it for
  query filtering would need a new CEL-to-pushdown translator that
  doesn't exist, where HotChocolate already gives that translation for
  filtering natively. **Explicit user direction: don't build a dedicated
  query-store abstraction now** — if `IQueryable`-over-Entity-Store
  filtering ever proves insufficient, extend the already-designed CQRS/
  Projections mechanism (`ADR-015`/`016`, Phase 9) then, not preemptively.
- **User also asked for a full repo-wide staleness review pass** (not
  just `features/*.md`'s stale Gherkin/`ADR-054`–`074` gap) —
  scope/timing not yet agreed; likely a `parallel-batch-dispatch.md`-
  shaped job once the above docs land.
