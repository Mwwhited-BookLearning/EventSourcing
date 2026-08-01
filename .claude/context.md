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
  tree has uncommitted doc-consistency fixes plus `TODO.md`/`.claude/
  context.md`/two new protocols (see `git status`) — commit or continue
  from there.
- Just created, this session: `TODO.md` (active-work tracker, replacing
  `CLAUDE.md`'s old inlined "Propagation status" list), this file, and
  `.claude/protocols/todo-tracking.md` + `context-handoff.md`.
- **In progress right now: talking through `docs/10-open-questions.md`
  row 12/13/14 and several `TODO.md` items directly with the user,
  reaching real design decisions (below) that are NOT YET written into
  any ADR, pattern doc, or `TODO.md` update.** If this session ends
  before that write-up happens, the next session's first job is turning
  the "Working notes" below into actual ADRs/docs, then clearing this
  section back down to a plain status line.
- **Read `TODO.md` for what's mechanically outstanding** (doc propagation
  gaps) and `docs/10-open-questions.md` for what's still an undecided
  fork — but note several rows there now have real answers agreed in
  conversation (see below) that haven't been written back yet.

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
- **Real design decisions reached in conversation, 2026-07-31, not yet
  written into any ADR/doc:**
  - **Row 13 (feature-flag/config contradiction)**: resolved as "not
    actually a contradiction" — a custom `IConfigurationProvider`
    (DB-backed, or Azure App Configuration-style) chained alongside the
    existing static providers, using .NET's normal reload-token
    mechanism, gives `ADR-038`'s instant/no-restart flag toggle without
    violating `ADR-041`'s "stays `Microsoft.Extensions.Configuration`"
    decision (which already embraces chaining live providers, e.g. Key
    Vault, for secrets) or `ADR-058`'s explicitly-left-open config
    source. Still needs an ADR settling: where flag state lives (leaning
    toward a reserved Event Log event per `ADR-067`'s control-plane-
    action pattern, for a free audit trail, rather than a bespoke admin
    table), the propagation mechanism/latency (poll vs. push), the exact
    flag/static-config boundary, and per-tenant scope under `ADR-075`'s
    silo model.
  - **Row 14 (multiple Router/UpcastMaterializer/outbox-pump instances)**:
    resolved as single-active-worker via **leader election with a
    database-backed lease row** (not a quorum system like etcd/
    ZooKeeper — unnecessary, since each site already has one trusted DB
    per `ADR-075`'s silo model, and production is Docker Compose per
    `ADR-026`, not Kubernetes, so no cluster-orchestrator election
    primitive exists to lean on anyway). Same shape as Azure's Leader
    Election pattern, adapted from a blob lease to a DB lease row for
    Postgres/SQL-Server portability (`ADR-004`). Not yet written as an
    ADR.
  - **Row 12 (EF Core migration application/race)**: resolved as EF Core
    **migration bundles** (`dotnet ef migrations bundle`, Microsoft's own
    recommended production path since EF Core 6) run as a single deploy-
    time step — no replica ever calls `Database.Migrate()` at startup,
    which is what removes the race entirely. Combined with the user's
    "final-state" idea: EF Core stays the C# authoring source
    (migrations, `ADR-038`'s Expand/Contract discipline) and generates
    SQL (`dotnet ef migrations script`); a provider-native declarative
    tool applies it — **DACPAC/`SqlPackage` for SQL Server**, **pgschema**
    for Postgres (verified — **not** `pgpkg`, which turned out to be a
    pl/pgSQL function-management tool, not a schema-migration tool; a
    near-miss caught by checking the actual project before citing it).
    Not yet written as an ADR.
  - **Data-model ownership convention**: already landed as a new bullet
    in `CLAUDE.md`'s "Conventions established so far" — the ADR that
    adds/changes a persisted field owns its name/shape and must update
    `docs/data/*.md` + `DbSet` registration in the same pass. This one
    IS done, not pending.
  - **`08-build-plan.md` restructuring**: agreed direction — replace
    fixed `Phase N` labels with a dependency-checklist model (each item
    declares its own prerequisite ADRs/items; display order/grouping is
    derived via topological sort, not hand-assigned) so adding a new
    capability never again requires renumbering or risks being skipped
    the way `ADR-050`–`075` were. The existing PlantUML dependency graph
    already models the right relationships through ~`ADR-048`; this
    reframes it as data instead of hand-maintained edges + backfills the
    missing ADRs. Not yet executed.
  - **The missing GraphQL-pushdown doc** (replacing `04-odata-filter-
    pushdown.md`): confirmed as a **query-pattern** doc, not a projection
    or CEL-based one. Filtering follows the Query pattern (GraphQL
    `Query` → HotChocolate `[UseFiltering]` → `IQueryable<Entity>` →
    same `IJsonPathTranslator` pushdown `ADR-037` already says survives).
    Projection (`[UseProjection]`, field-shaping) is a separate, mostly-
    free GraphQL bonus, not what this doc replaces. CEL stays scoped to
    upcast mapping only — reusing it for query filtering would need a
    new CEL-to-pushdown translator that doesn't exist, where HotChocolate
    already gives that translation for filtering natively. **Explicit
    user direction: don't build a dedicated query-store abstraction now
    — if `IQueryable`-over-Entity-Store filtering ever proves
    insufficient, extend the already-designed CQRS/Projections mechanism
    (`ADR-015`/`016`, Phase 9) then, not preemptively.** Doc not yet
    written.
  - **User also asked for a full repo-wide staleness review pass**
    (not just `features/*.md`'s stale Gherkin/`ADR-054`–`074` gap) —
    scope/timing not yet agreed; likely a `parallel-batch-dispatch.md`-
    shaped job once the above ADRs/docs land.
  - **Row 1 (OFAC/SAR screening)**: resolved as a framework-style
    extensibility seam, `ISanctionsScreeningProvider` shaped exactly
    like `ADR-057`'s `IErasureKeyStore` (keyed-DI, multiple pluggable
    backends per `AppId`), invoked as an automated detector feeding
    `ADR-042`'s existing `AuthorityStatus` gate — not a new review
    pipeline. **One sub-question still open, pending confirm**: core
    Duplex (like `IErasureKeyStore`, genuinely universal) vs. scoped to
    the KYC/Meridian application's own composition root (`ADR-041`/
    `ADR-059`) since OFAC/BSA is AML/KYC-specific, not universal.
    Recommended and currently leaning: **Meridian-scoped, not core.**
  - **Row 2 (GDPR breach notification)**: resolved as **out of framework
    scope entirely** — an external legal/business process, not a
    functional requirement. `ADR-045`/`ADR-019` already supply the only
    thing a framework should own (the forensic evidence); the breach
    register, notification-worthiness assessment, and 72-hour authority
    filing are a compliance team's process on top of that. Needs: a
    short compliance-note addendum on `ADR-045`, and updates to the two
    domain READMEs' (clinical-trials, digital-identity-kyc) Special
    Concerns sections that still list this as open. Neither done yet.
