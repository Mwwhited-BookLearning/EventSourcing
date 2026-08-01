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
principle: never lose or corrupt data. All 75 ADRs (`docs/adrs/adr-001`
through `adr-075`) are Accepted — the *decisions* are essentially done;
what's left is propagating them consistently across ~150 files with no
compiler to catch drift, which is why internal consistency matters more
here than in most repos.

## Current state

*(update this section's content, not just its presence, every session —
stale numbers here are worse than none)*

- As of **2026-07-31**: HEAD is `0bd157d` ("update designs"). Working
  tree has substantial uncommitted work this session (see `git status`)
  — commit or continue from there.
- **79 ADRs now exist** (`ADR-076`–`079` added this session, resolving
  `docs/10-open-questions.md` rows 12/13/14/1 respectively; `ADR-045`
  gained an additive addendum resolving row 2). All five rows are now
  struck through in `docs/10-open-questions.md`. `CLAUDE.md`'s ADR count
  is updated to match.
- Also created, this session: `TODO.md` (active-work tracker, replacing
  `CLAUDE.md`'s old inlined "Propagation status" list), this file, and
  `.claude/protocols/todo-tracking.md` + `context-handoff.md`.
- **Read `TODO.md` for what's mechanically outstanding** — it now
  includes the propagation debt the four new ADRs themselves created
  (two domain-README updates for `ADR-079`/`ADR-045`'s addendum; two new
  entities — `FeatureFlagState`, `LeaderLease` — added to `docs/data/
  schema-registry.md` already, but still missing from the drift table's
  `DbSet` list and from `08-build-plan.md`, which has no phase for any
  ADR past `050`).
- **Still open, not yet executed** (see "Working notes" below for full
  detail): the `08-build-plan.md` dependency-checklist restructuring,
  the missing GraphQL query/filter-pushdown doc replacing `04-odata-
  filter-pushdown.md`, and the user's request for a full repo-wide
  staleness review pass.

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
- **Design decisions reached in conversation, 2026-07-31, now promoted
  into real ADRs — no longer duplicated here, see the ADRs themselves**:
  `ADR-076` (EF Core migration bundles + optional DACPAC/pgschema apply),
  `ADR-077` (dynamic feature-flag configuration provider — flag state as
  a reserved Event Log event, polled, `AppId`-scoped), `ADR-078`
  (database-lease leader election, per worker role, not a quorum
  system), `ADR-079` (sanctions-screening seam, scoped to KYC/Meridian's
  own composition root, not core Duplex — the first domain-scoped
  extension point in this design), and `ADR-045`'s addendum (GDPR breach
  notification ruled out of framework scope). Remaining follow-up from
  these four is tracked in `TODO.md`, not here.
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
