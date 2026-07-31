# Protocol: tracking active work in TODO.md

## What `TODO.md` is for

A concrete, already-decided action item not yet done — a doc that needs
rewriting, a diagram that needs drawing, a rename, a propagation fix.
Explicitly **not**:
- A design fork not yet decided — that's
  [`docs/10-open-questions.md`](../../docs/10-open-questions.md). The
  test: if the *decision* is still open, it's a question, not a TODO. If
  the decision is made and only the doing is left, it's a TODO.
- A record of work already finished — that's
  [`docs/changes/{date}.md`](../../docs/changes). `TODO.md` never
  accumulates history; it only ever shows current active work.

One authoritative list, at the repo root. Nothing else in the repo
should restate its contents — same reasoning
`docs/10-open-questions.md` already applies to itself. Link to it
instead of copying items into `CLAUDE.md`, a design doc, or anywhere
else.

## Adding an item

- Add it the **same pass** you find it — don't let it live only as a
  sentence buried in a review's own output or a subagent's report.
  That's exactly how items got lost before this file existed (see
  `TODO.md`'s own `ChannelOrigin.Origin`/`OriginId` entry: flagged in
  `CLAUDE.md`'s prose for a while, pointed at a "Propagation status"
  list that never actually contained it).
- Write it so someone with **zero session memory** can act on it:
  which file(s), what's wrong or missing, and — if known — what the fix
  looks like. Cite the ADR/doc that makes it actionable, not just "this
  seems off."
- One checklist item per concrete task. If a review surfaces ten related
  but separable issues, that's ten items (or one item with ten sub-
  bullets if they'd realistically all get fixed in the same pass) — not
  one vague item that hides nine of them.

## Completing an item

1. Do the work.
2. **Delete the item from `TODO.md` entirely** — don't strike it
   through or archive it in place. Unlike ADRs and `docs/10-open-
   questions.md`, this file has no history-preservation goal; it should
   only ever show what's still outstanding *right now*.
3. Add a line to `docs/changes/{today's date}.md` describing what
   changed and why (create the file if today doesn't have one yet —
   match the header/section style already used in
   `docs/changes/2026-07-30.md`). This is where the "history" `TODO.md`
   deliberately doesn't keep actually lives.
4. If finishing the item reveals a new gap or a genuine design fork, add
   a fresh `TODO.md` item or `docs/10-open-questions.md` row in the same
   pass — don't let a review's own findings evaporate the way the
   `ChannelOrigin.Origin` collision did.

## Batching large items

Some `TODO.md` items are big enough to need splitting across parallel
agents (e.g. reworking 13 domains' Salt mockups to match a template
change). Use `parallel-batch-dispatch.md`'s process: group by disjoint
file ownership, give every agent the same shared context, consolidate
centrally, spot-check before marking the item done and removing it.

## Why this file exists

`CLAUDE.md`'s "Propagation status" section used to inline this exact
kind of list directly inside a standing-conventions file that reloads
into every session. It grew every pass, had no clear "done" signal
(nothing was ever actually removed from it), and at least one item
(`ChannelOrigin.Origin`/`OriginId`) got flagged in passing and then
silently never made it into the list at all. `TODO.md` fixes the "no
removal" problem specifically: an item's whole lifecycle — add, work,
remove, log — lives in one place, and `CLAUDE.md` just links to it
instead of carrying the content itself.
