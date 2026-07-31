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
  tree had uncommitted doc-consistency fixes from this session (see
  `git status`) — commit or continue from there.
- Just finished: a full review pass over the two chosen domains'
  feature-doc batch (clinical trials + device telemetry, digital
  identity/KYC — each now 4 feature docs / 3 workflows), which fixed 8
  broken relative links, one `AppId` typo, one missing pattern
  cross-link, and brought `docs/changes/2026-07-30.md` + `CLAUDE.md` up
  to date with what that batch actually shipped.
- Just created, this session: `TODO.md` (active-work tracker, replacing
  `CLAUDE.md`'s old inlined "Propagation status" list) and this file,
  plus their protocols under `.claude/protocols/`.
- **Read `TODO.md` for what's actually outstanding** — as of this
  writing its largest/oldest item is reworking the 13 considered-not-
  chosen domains' Salt mockups to match a template tweak that only the
  2 chosen domains' newest docs picked up.

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
