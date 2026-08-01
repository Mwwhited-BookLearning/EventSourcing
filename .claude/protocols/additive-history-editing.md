# Protocol: additive-history editing (ADRs are history, not editable state)

## The rule

When a later decision changes an earlier one, **don't delete the old
text — strike it through** (`~~...~~`) and add "Superseded by
`ADR-XXX`" (or "Corrected, this session" for a same-session fix) right
next to it. A reader should be able to see both what was decided
originally and what changed, in place, without needing git history.

```
- ~~Role *assignment* is identity-provider state, not an event-sourced
  core-engine concern~~ — **superseded by `ADR-067`**: a role grant now
  publishes a reserved event in the core Event Log itself...
```

## The one exception

If the earlier content was written **this same integration effort and
never shipped/built**, a clean rewrite in place is fine instead of a
strikethrough — there's no real "history" to preserve yet, since
nothing downstream depended on the original version. Real examples:
`ADR-018`'s upcast mechanism (revised in place from OData to CEL,
before anything downstream had actually been built against the OData
version) and `ADR-031`'s "telemetry channels" (revised in place to
"streaming channels" once audio/video turned out to be the same
mechanism). Judgment call — when in doubt, strike through rather than
silently rewrite; it costs nothing and removes the ambiguity.

## Where this has actually mattered this session

- Three ADRs (`046`, `047`, `067`) disagreed about who owns
  `Role`/`UserPermission` — the later ADR (`067`) won, the earlier two
  got struck through with a pointer to it, rather than picking one
  arbitrarily or silently editing the earlier ones to match without a
  trace.
- `docs/10-open-questions.md` is the one deliberate **exception** to
  this whole protocol: a resolved row is deleted outright, not struck
  through, because the resolving ADR is already the permanent record of
  what got resolved and how — retaining a second, struck-through copy in
  the tracker would just duplicate it. That file's own header explains
  the reasoning; don't assume every tracker in this repo follows the
  strikethrough convention just because ADRs do.
- `references.md`'s reference-only entries can flip to adopted later
  (SPIFFE/SPIRE, YARP, content-addressable storage all did) — the
  original rejection reasoning stays visible, annotated with what
  changed to trigger the reversal, not overwritten.

## Never do this

- Don't silently rewrite an Accepted ADR's Decision section to match a
  later change with no marker at all — this is exactly the bug a
  design review found three times this session (`ADR-018`, `ADR-020`,
  `ADR-046`/`047`) and had to fix after the fact. Mark it the moment
  you make the change, don't leave it for a future review pass to
  catch.
- Don't strike through and then also delete the surrounding context
  that explains *why* the original decision seemed right at the time —
  that reasoning is often exactly what a future reader needs to judge
  whether the new decision is actually final, or might flip again.
