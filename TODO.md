# TODO

A live tracker for **concrete, already-decided work that just hasn't
been done yet** — distinct from both other live trackers in this repo:

- [`docs/10-open-questions.md`](docs/10-open-questions.md) is for a
  design fork **not yet decided** — the question itself is still open.
- **This file** is for a task where the decision is already made (a doc
  needs rewriting, a diagram needs drawing, a terminology collision
  needs resolving) and only the doing is left.
- [`docs/changes/{date}.md`](docs/changes) is the narrative history of
  work **already completed** — where an item here goes once it's done.

**Full workflow (adding/completing items, batching large ones) is in
[`.claude/protocols/todo-tracking.md`](.claude/protocols/todo-tracking.md)
— read it before touching this file.** Short version: add an item the
same pass you find one; when it's done, delete the item here and add a
line to today's `docs/changes/{date}.md` instead.

**This is the authoritative list of active work** — per the same
reasoning `docs/10-open-questions.md` already applies to itself, do not
restate this list's contents elsewhere in the repo (including in
`CLAUDE.md`); a duplicated copy just drifts stale. `CLAUDE.md` points
here instead of inlining.

## Active

Grouped into phases by actual dependency, not just topic — a later
phase's items reference or build on an earlier phase's (or an earlier
item within the same phase's) output where a real dependency exists,
not the other way around. Within a phase, items are otherwise
independent of each other and can be done in any order, or dispatched
in parallel (`.claude/protocols/parallel-batch-dispatch.md`) — Phase 3
in particular is sized for that. Nothing here is a priority ranking
beyond the dependency ordering itself; pick whichever phase suits
available time.

### Phase 1 — GraphQL/API-contract rewrite cluster

Internally sequenced — do these roughly in this order, since each later
item is easier to get right once the earlier ones exist to reference,
though none is strictly blocked from starting early.

- [ ] **`06-solution-structure.md`'s detailed DI-wiring code sketches
  predate `ADR-041`** (explicit composition) and mostly predate
  `ADR-054` onward's new projects (a webhook dispatcher, a rate limiter,
  an SDK-generation step, device-input client packages) — flagged stale
  in the file's own banner, not silently wrong, but not rewritten either.
  Not blocked on anything else in this file — the entity homes this
  item used to wait on were settled this session.
- [ ] **Every banner'd `docs/features/*.md` file's Gherkin scenarios are
  themselves still unchanged** (`400`→`202`+`SchemaStatus`, OData→GraphQL
  syntax) — the banners say what's stale, they don't fix the scenarios.
  Separately, **none** of the `docs/features/*.md` files reference any
  ADR past `ADR-053` — the entire `054`–`074` batch has zero feature-
  doc/Gherkin coverage. Do last in this cluster — needs the corrected
  contract shape above to write accurate scenarios against.

### Phase 2 — Build-plan restructuring

Benefits from Phase 1 (knowing what's actually built) being settled
first, though it's a structural change to the tracking document itself,
not new design content. The data-model accuracy this phase used to also
wait on was settled this session.

- [ ] **`08-build-plan.md` has no phases for `ADR-050`–`ADR-093`** —
  every capability from per-tenant rate limiting through this session's
  batch (migration bundles, dynamic feature flags, leader election,
  the sanctions-screening seam, RFC 3161 timestamping, i18n/l10n
  architectural scope, mechanism-level OTel instrumentation, Event Log
  archival, and more) has no build-plan entry. `ADR-057` (erasure) and
  `ADR-062` (package distribution) most need real exit criteria before
  anything downstream is built. Candidate for the dependency-checklist
  restructuring agreed in conversation (see `.claude/context.md`) —
  each item declares its own prerequisite ADRs, display order derived
  by topological sort — rather than more numbered phases tacked onto
  the end.

### Phase 3 — Large content batch

Independent of every phase above; the single biggest item in this file,
and the one best suited for parallel dispatch
(`.claude/protocols/parallel-batch-dispatch.md`) given its 13 disjoint
file-ownership units.

- [ ] **13 considered-not-chosen domains' feature docs use the pre-tweak
  single-screen Salt mockup.** `.claude/templates/feature-doc-
  template.md`'s Salt-mockup guidance was tightened mid-session (2026-
  07-30) from a single static mockup to a required 2–4 screen sequential
  flow, but the tweak only got applied going forward — to the 4 feature
  docs added to the two chosen domains (clinical trials, digital
  identity/KYC) afterward. The other 13 domains' single feature doc each
  (biobanking, brokerage-capital-markets, digital-forensics-evidence-
  custody, dscsa-pharma-supply-chain, education-credentials, government-
  case-management, industrial-iot-predictive-maintenance, insurance-
  telematics, itar-export-controlled-defense-data, logistics-chain-of-
  custody, pharmacovigilance, public-health-surveillance, utilities-
  smart-metering) were never revisited. Not a correctness bug — a
  structural inconsistency a reader comparing two domains' mockups would
  notice.
