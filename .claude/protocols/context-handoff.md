# Protocol: keeping `.claude/context.md` current

## Purpose

A single file where a fresh agent (or a human) can find out where this
project stands, without replaying git-log archaeology or losing
information the way an earlier, unresumable conversation did. It's a
**snapshot**, not a log — overwrite it in place each time, don't append
to it (that's what `docs/changes/{date}.md` is for).

## When to update it

- At the end of any session that made real progress (finished a
  `TODO.md` item, wrote or decided something, changed direction).
- Whenever asked to summarize "where are we" or "what was I doing" —
  update the file as part of answering, don't just answer in the chat
  and leave the file stale.
- Before a session is likely to end mid-task, if there's a real chance
  the next session starts cold with no memory of this one.

## What goes in it vs. what doesn't

**Goes in:**
- Current git `HEAD` and whether the tree is clean.
- What changed most recently and why (one or two sentences — link to
  `docs/changes/{date}.md` for the full narrative, don't re-tell it).
- What's actively in flight — point at `TODO.md`'s top item(s), don't
  copy them in; a copy here just drifts stale the moment `TODO.md`
  changes.
- A short "how to resume cold" reading order.
- Working understanding that exists **only in the current conversation
  and nowhere else written down yet** — a stated preference, a scope
  decision made but not yet reflected in a doc, a "we ruled this out and
  here's why" that hasn't made it into `docs/10-open-questions.md` or an
  ADR yet. This is the category most worth capturing, because it's the
  category that's otherwise gone the moment the conversation ends.

**Doesn't go in — link instead of duplicating:**
- A decision (that's an ADR).
- An open, undecided fork (`docs/10-open-questions.md`).
- An active, already-decided task (`TODO.md`).
- Narrative history of completed work (`docs/changes/{date}.md`).

If content here would duplicate one of those, it means the "working
note" has matured into something durable — promote it to the right file
and link to it instead of leaving a second copy here. Same drift
reasoning `docs/10-open-questions.md` and `TODO.md` already apply to
themselves.

## Format

Short — a two-minute dashboard, not a design doc. Five tight sections,
in this order:
1. **What this project is** — one paragraph, rarely changes.
2. **Current state** — as of a specific date/commit; overwrite the
   content every update, don't just bump the date.
3. **Actively in flight** — pointer to `TODO.md`, one-line summary of
   its top item, not a copy of the whole list.
4. **How to resume cold** — a short numbered reading order.
5. **Working notes not yet written down elsewhere** — the ephemeral
   category above; prune an entry once it's been promoted to a durable
   doc.

## Why this exists

This session's own experience: an earlier conversation couldn't be
resumed, and reconstructing "where things stood" required grepping git
history, re-reading `CLAUDE.md`'s propagation notes, and spawning review
agents just to answer "where was I?" That reconstruction cost is exactly
what this file exists to avoid paying every time.
