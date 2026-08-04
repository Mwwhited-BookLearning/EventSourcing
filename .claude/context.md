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

- As of **2026-08-04**, working on branch **`design/service-level-
  agreement`** (created this session, off `main` at a clean tree). `main`
  itself carries the 2026-07-31–2026-08-03 build-plan-rework session
  (all 93 ADRs Accepted, `TODO.md` had 10 active items at that point) —
  see that range of `docs/changes/*.md` for its full narrative, not
  re-told here.
- **This session (design-discussion-led, not a `TODO.md` pass)**:
  confirmed the existing dual-`TelemetryChannel` mechanism (`ADR-031` +
  the clinical-trials domain's "Dual-channel live-safety vs. standard
  persistence" special concern) already supports mixed near-real-time/
  delayed-review SLAs with no design change, then named IONM and
  polysomnography as the two concrete use cases in
  `docs/domains/clinical-trials-device-telemetry/README.md`. That
  surfaced a distinct question — tracked event acknowledgment (not just
  delivery) — first weighed in a comparison doc
  (`docs/comparisons/event-response-acknowledgment.md`) recommending
  deferral, then **corrected and resolved as `ADR-094`**: a generic
  `RespondsToEventId` envelope field (the eighth relationship-shaped
  one, Correlation Identifier pattern) + opt-in
  `EventTypeDefinition.ExpectedResponse`, escalation policy left to the
  application. The correction that unlocked this: IONM/polysomnography
  were domain-*example* instances, not the framework decision's own
  shape — waiting for their specific numbers to converge was the wrong
  test; whether the relationship itself generalizes was the right one.
  `docs/10-open-questions.md` is empty again (row added and resolved
  same session). Full narrative: `docs/changes/2026-08-04.md`.
  `TODO.md`'s Active section is empty as of this session.
- **`ADR-094` committed and pushed** (three commits on
  `design/service-level-agreement`, up through `eab72b7`, all pushed to
  `origin`). Two follow-up audit passes each caught one real, small,
  pre-existing propagation gap by actually checking files rather than
  trusting an ADR's own claims (`projections-client`/
  `expected-response-watcher-client` missing from `auth.md`'s seeded-
  clients table; `LeaderLease.WorkerRole`'s enum missing
  `ExpectedResponseWatcher`) — both fixed in place, both pushed.
- **Workflow D added on direct request, not yet committed**: the
  clinical-trials domain grew from 4 feature docs/3 workflows to 5/4 —
  `features/intraoperative-monitoring-and-alert-response.md`, IONM's
  first real domain-level exercise of `ADR-094`. Propagated into the
  domain `README.md`, `CLAUDE.md`'s domain-doc bullet, and `ADR-094`'s
  own Consequences. Full narrative: `docs/changes/2026-08-04.md`.
  Confirm with the user before committing/pushing this increment.
- **Standing lessons from the 07-31–08-03 session, still worth carrying
  forward** (condensed — see that range's `docs/changes/*.md` for the
  full incidents): an agent's own "looks right" report on a PlantUML
  diagram it wrote is not proof it renders — a backslash-escaped-quote
  bug hid in 9 self-reported-complete diagrams last session; always
  actually render-check, don't trust the transcript. Verify a
  propagation claim against the real file, never an ADR's own
  Consequences section saying something "is done." Grep a whole file
  for a topic before assuming one updated bullet means the whole file is
  consistent.
- **Other lessons from this session worth carrying forward**:
  **(1)** verify a propagation claim against the actual file, never trust
  an ADR's own Consequences section saying something "is done"; **(2)**
  grep a whole file for a topic before assuming one updated bullet means
  the whole file is consistent — partial-edit bugs hide in the
  untouched spots; **(3)** a same-session rewrite isn't yet-verified
  ground truth just because it's more recent than what it cites — cross-
  check it the same as anything older; **(4)** when a large TODO item
  turns out to bundle two genuinely different jobs (fix-what's-stale vs.
  write-net-new-content), split them explicitly rather than letting the
  harder half quietly ride along or get dropped; **(5)** offering an
  explicit choice before a large, effort-heavy rewrite (full-restructure-
  now vs. lighter-touch vs. defer) consistently got a clear, fast answer
  rather than stalling — keep doing this for the next large item, don't
  assume silent authorization from an earlier "keep going"; **(6)** when a
  TODO item frames something as "an unresolved contradiction," check
  whether the ADR itself already resolved it via an in-place amendment
  before assuming a new decision is needed — struck-through text reads
  easy to skip (this is exactly how `masking.md`'s "strategy contradiction"
  turned out not to be one at all).

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

- The user wants to be asked before large, effort-heavy content rewrites
  get started unilaterally — offer explicit options (e.g. "full rewrite
  now / lighter version / defer"), don't just do it. This got a clear,
  fast answer every time it was tried this session (the build-plan
  restructuring, the Salt-mockup batch) — keep doing it, don't start
  assuming a much earlier "keep going" still covers a new large item.
  Smaller, unambiguous fixes (broken links, typos, missing cross-
  references, a stale field name found mid-task) are fine to fix directly
  during a review pass without asking first.
- The two-tier domain depth (13 domains at 1 feature doc each vs. 2
  chosen domains at 4 feature docs / 3 workflows each) is intentional,
  not unfinished — don't mistake the 13 shallower domains for a batch
  that still needs finishing to 4 docs each. (Their Salt mockups are now
  at the same 2–4-screen depth as the chosen domains' — only the feature-
  doc *count* per domain still differs, deliberately.)
- **A full repo-wide staleness review pass beyond what this session's
  ADR-focused reviews have covered** was asked for at one point but never
  scoped/scheduled — still genuinely open, likely a `parallel-batch-
  dispatch.md`-shaped job whenever it's picked up. Don't assume it's been
  done just because several large, related sweeps (the `docs/features/*.md`
  Gherkin rewrite, the build-plan restructuring) have since landed.
