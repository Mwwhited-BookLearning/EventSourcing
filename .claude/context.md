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

- As of **2026-08-03** (continuing one long working session that started
  2026-07-31): HEAD was `a602317` ("updating design") as of the last
  check, moved several times via commits not made by this conversation
  (an external process/terminal checkpointing the work; harmless, just
  don't assume it's still current — re-check `git log`/`git status`).
  Working tree has further unstaged edits from this session's work-
  through of `TODO.md` — see `git status`.
- **`docs/10-open-questions.md` is EMPTY** and **`08-build-plan.md` no
  longer uses fixed `Phase N` numbering** — cite its items by name only
  (`` `08-build-plan.md`, "Event-Type Security" ``, never a number).
  **All 93 ADRs are Accepted; the original 6-phase TODO.md restructuring
  plan from earlier this session (quick fixes → data-model → diagrams →
  GraphQL/API-contract cluster → build-plan restructuring → 13-domain
  Salt-mockup rework) is fully complete.** Full narrative across
  `docs/changes/2026-07-30.md` through `2026-08-03.md` — not repeated
  here; skim the latest one or two for what actually happened, this
  section only tracks what's still open.
- **`TODO.md` is down to 3 small, mutually-independent items**, found
  mid-session rather than pre-planned — read `TODO.md` directly, don't
  rely on this summary: `docs/features/masking.md`'s wrapper needs
  `ADR-057`'s `erased` branch (bundled with a genuine unresolved
  contradiction over how many masking strategies v1 actually supports);
  `docs/features/*.md` has zero coverage for `ADR-054`–`074` (needs its
  own scoping pass on full-doc-vs-section before starting, not yet done);
  and `RequiredPublishClaim`/`RequiredReadClaim` (pre-`ADR-050` naming)
  is still presented as current in several domain feature docs, `05-
  schema-registry-and-spec-generation.md`'s body, `ADR-013`'s error
  table, and `streaming-and-attachments.md`'s `TelemetryChannel` field —
  not yet scoped with a full repo grep.
- **Lessons from this session worth carrying forward, not re-learning**:
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
  assume silent authorization from an earlier "keep going."

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
