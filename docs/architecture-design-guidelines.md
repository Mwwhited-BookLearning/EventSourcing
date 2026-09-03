[← Document index](../README.md)

# Architecture & Design Guidelines

**Purpose**: one reference for checking a piece of work — a new ADR, a
pattern doc, a domain doc, a code change — against the cross-cutting
conventions this repo has already established, consolidated here rather
than scattered across `CLAUDE.md` and six separate protocol files.
Written per direct request, aimed specifically at **compliance/
consistency checking**: this is the doc a review (human or AI) should
check work against, and the doc Phase 5's linting/analysis tooling is
meant to enforce wherever a rule below is mechanically checkable.

**How this relates to what already exists**:
- `CLAUDE.md` is the standing index for AI sessions working in this
  repo — layout, doc-kind list, a short "conventions established so
  far" section. This doc doesn't replace that; it reorganizes the
  cross-cutting *rules* half of it into one compliance-oriented
  reference, and adds code-level conventions `CLAUDE.md` doesn't carry.
- `.claude/protocols/*.md` hold the **step-by-step process** for a
  specific recurring task (how to edit an ADR additively, how to file a
  bug report, how to batch large work across parallel agents). Read the
  protocol when *doing* the task; check this doc when *reviewing* one,
  or when you need the cross-cutting rule without the full process
  walkthrough.
- Every rule below cites its source protocol/ADR — this doc doesn't
  invent anything new, it organizes what's already decided.

Each section ends with an **Enforceable** marker: 🔧 (mechanically
checkable — a real candidate for Phase 5's linting/analysis tooling) or
👁️ (needs human/AI judgment — a review checklist item, not a lint rule).

## 1. Which document kind, which template

| You're writing... | Home | Template |
|---|---|---|
| A decision already made | `docs/adrs/adr-NNN-slug.md` | `.claude/templates/adr-template.md` |
| A general, portable pattern this design applies | `docs/patterns/{slug}.md` | Match `docs/patterns/fault-injection-chaos-engineering.md`'s structure (The pattern + Source, Also known as, When you'd reach for it, Cost, How this application uses it) |
| Two+ patterns composing at one specific point | `docs/patterns/interactions/{slug}.md` | Match `docs/patterns/interactions/gated-authoritative-publish.md` |
| A genuine multi-option fork, not yet decided | `docs/comparisons/{slug}.md` | `.claude/templates/comparison-doc-template.md` |
| One engine feature (context/diagrams/Gherkin) | `docs/features/{slug}.md` | `.claude/templates/feature-doc-template.md` |
| One domain feature (same depth, one industry) | `docs/domains/{domain}/features/{slug}.md` | `.claude/templates/feature-doc-template.md` |
| A domain's own README (ADRs/regs/glossary) | `docs/domains/{domain}/README.md` | `.claude/templates/domain-doc-template.md` |
| An adopted/considered off-the-shelf library | `docs/libraries/{platform}/{library}.md` | Match an existing entry under that platform folder |
| A genuine runtime defect, found and fixed | `docs/bugs/{scope}/{tier}/{summary-title}.md` | `.claude/protocols/bug-report-tracking.md`'s four sections |

**Enforceable**: 👁️ (choosing the right kind is a judgment call; a
missing required section within a chosen kind is 🔧-checkable in
principle, per the feature-doc template's fixed section list).

## 2. Research & citation discipline

Source: `.claude/protocols/verify-before-citing.md` — "this project's
single most repeated standing instruction."

1. **Search for real prior art before designing anything new**, even
   when the request doesn't name a standard. Two real examples: the
   ticket-exchange mechanism (`ADR-040`) was designed only after
   searching turned up CAS service tickets, RFC 7662, and CDN
   signed-URL conventions; the silo-vs-pool tenancy decision was
   grounded in AWS/Azure's own real terminology before anything was
   decided.
2. **Verify every citation via WebFetch/WebSearch before writing it
   down** — a pattern name, an RFC number, a library's maintenance
   status. Don't recall from memory and assume correct.
3. **A close-call synonym gets checked, not assumed.** Real examples
   this repo found weren't true synonyms on inspection: KYC vs. CDD,
   Clearinghouse vs. CCP, De-identification vs. Anonymization. If
   verification comes back "related but distinct," say so explicitly.
4. **If you can't verify something confidently, don't write it down.**
   A missing citation is honest; a wrong one erodes trust in every
   other citation in the same doc.
5. Adopting something real gets a `docs/libraries/{platform}/*.md`
   write-up or a `references.md` row; **rejecting** something real
   still gets a `references.md` row (reference-only, with the specific
   reason) — so a later reader knows it was considered, not missed. A
   rejection can flip to adopted later if the original reason stops
   holding (SPIFFE/SPIRE, YARP, and content-addressable storage all did
   this) — check for a stale rejection before assuming something is
   still out of scope.

**Enforceable**: 👁️ entirely — no linter can verify a citation against
the real world.

## 3. ADR discipline — additive history, not editable state

Source: `.claude/protocols/additive-history-editing.md`.

- **When a later decision changes an earlier one, don't delete the old
  text — strike it through** (`~~...~~`) and add "Superseded by
  `ADR-XXX`" or "Corrected, [date/session]" right next to it. A reader
  should see both what was decided originally and what changed, in
  place, without needing git history.
- **The one exception**: content written the same integration effort
  and never shipped/built can be rewritten cleanly in place — there's
  no real history to preserve yet. Judgment call; strike through when
  in doubt, it costs nothing.
- **Never hardcode a future ADR's number** in a propagated doc — write
  "the queued X ADR," not a specific number, until that ADR actually
  exists. Backfill the real number once it does.
- **The ADR that adds or changes a persisted field/entity/table is that
  field's naming/shape authority — and must land the matching
  `docs/data/*.md` edit and `DbSet` registration in the *same pass*,**
  not deferred to a later sweep. This has been the single most common
  source of drift in this repo's history.
- **A repeated relationship pattern gets its own envelope-metadata
  field, never conflated with an existing one just because the shape
  looks similar.** Eight exist today (`parentEventIds`,
  `MaterializationOfEventId`, `TelemetryPointer`, `AttachmentRef`,
  `erasureScope`, `Signature`, the `Original*`/`ImportedFrom` trio, and
  `RespondsToEventId`) — ask explicitly what question a ninth would
  specifically answer before adding it.
- **Never silently rewrite an Accepted ADR's Decision section to match
  a later change with no marker at all** — this exact bug has been
  found and fixed multiple times in this repo's own history (`ADR-003`,
  `018`, `020`, `046`/`047`, and 18 more found in this session's own
  Phase 1 review). Mark it the moment you make the change.

**Enforceable**: 🔧 partial — a script can flag an ADR whose own body
contains no `~~`/"Corrected"/"Superseded" marker anywhere near text that
another ADR's index row claims supersedes it; genuinely judging whether
a *change* requires a marker is 👁️.

## 4. Standards adoption discipline

- **Never invent a bespoke mechanism when a real standard already
  fits; prefer buy over build for libraries the same way.** Record the
  outcome in `docs/references.md` either way — adopted, or considered-
  and-rejected with the specific reason — never let an evaluation
  silently disappear.
- **Say when something is only partially borrowed**, and disambiguate
  terminology collisions explicitly rather than hoping context makes it
  clear (e.g. "query parameter" vs. the HTTP `QUERY` method; UCAN's
  "attenuation" vs. this repo's own delegated-grant vocabulary).

**Enforceable**: 👁️.

## 5. Diagram convention

- **No external `!include` in any PlantUML diagram, ever** — hand-style
  C4 notation in plain PlantUML instead. `C4-PlantUML` fails silently
  (a blank or broken diagram, no readable error) in any renderer without
  live internet access, which is most local/offline setups. Applies to
  every PlantUML diagram in this repo, not just C4 ones.

**Enforceable**: 🔧 — a script can grep every `.puml`/fenced-plantuml
block for `!include` and flag any hit.

## 6. Verification discipline — "run the real thing"

- **A build/test pass succeeding is not the same bar as actually
  running the thing.** Real, otherwise-invisible bugs in this repo's own
  history were found only by running a real `AppHost`, a real
  Playwright playbook, or reading a real container's own logs — never
  by a green test suite or a code-review pass alone.
- **Run `dotnet test` with `--logger "console;verbosity=detailed"`**,
  not a terse default run — a plain run can silently eat the "Standard
  Output Messages" a test actually produced (container lifecycle logs,
  an inner exception's own detail), exactly the detail that has
  explained a real bug before.
- **For a runtime error, diagnose via real logs** (e.g. `docker logs`)
  and actually running the thing, not source review or an attached
  debugger alone.
- **File a `docs/bugs/{scope}/{tier}/{summary-title}.md` entry** for a
  genuine, previously-undiscovered defect found this way (not a design
  fork, not a doc/code drift with no runtime effect) — see
  `.claude/protocols/bug-report-tracking.md` for the four required
  sections and the required regression test (must be proven to fail
  red against the pre-fix code and pass green against the fix).

**Enforceable**: 🔧 for the `--logger` flag (a CI/local script can check
the invocation); 👁️ for everything else — no tool can verify "did you
actually run the real thing."

## 7. Code-level conventions

- **Constructor injection over service-locator/hand-rolled singletons;
  a singleton must be IoC-registered**, not a static/`Lazy<T>` instance
  reached for from arbitrary code. `PayloadMasker`'s one narrow,
  documented exception (resolving `IMaskingStrategy` by a runtime
  string key via `IServiceProvider.GetRequiredKeyedService`) is the
  kind of case that needs an explicit, written justification, not a
  precedent to reuse casually.
- **Humble Object for background workers**: pull the real logic out of
  a hard-to-test `IHostedService`/`BackgroundService` shell into a
  public static method (`RunOnceAsync`) taking explicit, sometimes-
  nullable collaborator parameters — a test calls it directly, no DI
  container needed. See `docs/patterns/humble-object-testable-core.md`.
- **Known Outcomes Are Not Exceptions**: a well-understood, expected
  outcome (not registered yet, already exists, lacks a claim) is a
  named result type a caller branches on, never a thrown-and-caught
  exception. See `docs/patterns/known-outcomes-are-not-exceptions.md`.
- **Explicit composition root, no reflection-based auto-discovery**
  (`ADR-041`) — every DI registration is a visible line near the entry
  point, including keyed services (one line per strategy).
- **No AutoMapper, no Newtonsoft.Json** (`ADR-041`) — explicit mapping,
  `System.Text.Json`.
- **No `--` inside an XML comment in a `.csproj` file** — causes
  MSB4025, a recurring mistake in this repo's own history. Scan for it
  before writing an XML comment in a project file.
- **Tag tests with a category/trait**: unit/integration/e2e, plus which
  ADR/role/domain/epic they exercise — `MSTest`'s `[TestProperty]`, the
  same mechanism `docs/bugs/*.md`'s own required regression-test tag
  uses (`[TestProperty("BugReport", "docs/bugs/...")]`).

**Enforceable**: 🔧 for AutoMapper/Newtonsoft package references, `--`
in `.csproj` XML comments, and (with a Roslyn analyzer) constructor-
injection-only/no-static-singleton patterns; 👁️ for Humble Object shape
and Known-Outcomes-vs-exception judgment calls.

## 8. Tracking-file discipline — one authoritative home per kind of fact

| Fact | Home | Lifecycle |
|---|---|---|
| Decided work not yet done | `TODO.md` | **Delete on completion** — don't leave a "done" narrative in place; log the full account in `docs/changes/{date}.md` instead |
| A design fork not yet decided | `docs/10-open-questions.md` | **Delete the row on resolution** — the resolving ADR is the permanent record; that day's changelog gets a one-line pointer, not a retained struck-through copy |
| Narrative history of completed work | `docs/changes/{date}.md` | Append-only, one file per date |
| Session-handoff snapshot | `.claude/context.md` | **Overwrite in place**, never append — a dashboard, not a log |
| A genuine runtime defect, found and fixed | `docs/bugs/{scope}/{tier}/{summary-title}.md` | One file per bug, written the same pass it's fixed |

**Never restate one tracker's contents inside another** (including
inside `CLAUDE.md`) — a duplicated copy just drifts stale the moment
the source of truth changes. Link to it instead.

**Enforceable**: 🔧 partial — a script can flag a `TODO.md` item whose
own text says "done"/"closed"/"is now" (a signal it should have been
deleted, not narrated) or a `docs/10-open-questions.md` row that's
struck through rather than deleted; genuinely picking the right home
for a new fact is 👁️.

## 9. Splitting large, multi-file work

Source: `.claude/protocols/parallel-batch-dispatch.md`.

- **Use parallel background agents** when work splits into ≥4 genuinely
  independent units (different files, no shared state) and each unit
  needs real judgment/research, not mechanical repetition.
- **Group by disjoint file ownership first, batch size second** — no
  two concurrently-running agents ever touch the same file.
- **Consolidate shared-file edits centrally, after the batch returns**
  — don't let agents touch a file every batch wants to touch
  (`docs/patterns/README.md`, `TODO.md`, `docs/10-open-questions.md`);
  merge it yourself instead.
- **Spot-check a sample of the actual output before trusting a "done"
  report** — an agent's summary describes what it intended to do, not
  necessarily what it did.

**Enforceable**: 👁️ — this is a process/orchestration discipline, not
something a static analyzer touches.

## 10. A found gap is not automatically a defect

Direct instruction, this session: **only resolve unilaterally what's
objectively, directly verifiable** — a doc's own correction note that
was never carried through, a cited class that provably doesn't exist, a
build-plan status that contradicts an ADR's own text. Everything else —
something that merely *looks* missing, thin, or duplicated — surfaces
as a question instead of a silent fix: a `docs/10-open-questions.md` row
if it's a genuine design fork, or a direct question if it's really "was
this on purpose." A thing that looks like an oversight may have been a
deliberate, documented scope decision (see `docs/comparisons/webdav-
library.md`'s deliberate skip, or `ADR-007`'s deliberate deferral) —
verify the doc doesn't already explain the absence before treating it
as a gap.

**Enforceable**: 👁️ entirely — this is a review-judgment discipline by
definition.

## 11. Build-plan tracking

- **A new capability gets a named item in `08-build-plan.md`**, not a
  fixed phase number — cite it by name, never by a number that could
  need renumbering later.
- **The build-plan's dependency diagram tracks status by fill color,
  updated in lockstep with the Implementation-status table's own Status
  column, in the same pass, every time**: no fill = Not started,
  `#palegoldenrod` = In progress, `#palegreen` = Done.

**Enforceable**: 🔧 — a script can diff the Implementation-status
table's Status column against the diagram's own fill-color state and
flag a mismatch.

## Summary: what Phase 5's tooling can realistically enforce

Genuinely 🔧-mechanical, worth real lint/analyzer configuration:
no `!include` in PlantUML; no AutoMapper/Newtonsoft package references;
no `--` in `.csproj` XML comments; the `--logger` flag on `dotnet test`
invocations (a script/CI check, not a compiler analyzer); a Roslyn
analyzer for constructor-injection-only DI (flag `new ServiceLocator`-
shaped code or a non-DI-registered mutable static). Everything else
above is 👁️ — a review checklist, not a lint rule — because it depends
on real-world verification, judgment about what a change means, or
cross-document consistency a static tool can't evaluate. Phase 5 should
scope itself to the 🔧 list and treat the rest as a human/AI review
aid, not attempt to force every rule above into a linter that can't
actually check it.
