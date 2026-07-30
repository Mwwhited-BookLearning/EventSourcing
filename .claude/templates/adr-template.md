# Template: ADR (`docs/adrs/adr-NNN-slug.md`)

## New ADR skeleton

```
[← ADR index](../07-adrs.md)

# ADR-NNN: <short, decision-stating title, not a topic label>

Status: Accepted

Context: <the real problem/gap that forced this decision — cite the
prior ADR/doc that exposed it, not a generic "we need to decide X">

Decision:
- <one bullet per real sub-decision, each independently checkable>
- <cite prior art/real standards BEFORE writing anything bespoke — see
  .claude/protocols/verify-before-citing.md>

Consequences:
- <what this changes downstream, including which other docs now need
  a matching update>
- <what's deliberately NOT solved here, and why>

**Compliance note** (a proving-ground compliance review, this
session): <only if genuinely applicable — see the rule below>
```

Then: add a row to `docs/07-adrs.md`'s index table, in ADR-number
order.

## Rules, not just formatting

- **Never hardcode a future ADR's number.** Write "the queued X ADR" in
  any doc that references a decision not yet written — ADR numbers are
  assigned by write order. Backfill the real number only once that ADR
  actually exists.
- **Search for prior art before designing anything new — always, not
  only when a standard is obviously relevant.** See
  `.claude/protocols/verify-before-citing.md`.
- **Never invent a bespoke mechanism when a real standard/library
  already fits.** Check for one before deciding to build.
- **A repeated relationship gets its own envelope field, never
  conflated with an existing one because the shape looks similar.**
  This design already has several (`parentEventIds`,
  `MaterializationOfEventId`, `TelemetryPointer`, `AttachmentRef`,
  `erasureScope`, `Signature`) — if a new one is tempting, ask
  explicitly what question it answers that none of the existing ones
  do, and say so in the ADR's Context.
- **Compliance note — only when genuinely applicable, never forced.**
  Add the bottom bullet only if a real regulation/standard drives the
  decision. Most ADRs are purely mechanical/internal and get no note —
  forcing one onto every ADR would be worse than omitting it. Cite only
  a real, verified regulation/standard (WebFetch the actual text if
  it's not already in `references.md`).
- **A new real-world citation gets a `references.md` row** the same
  pass it's first used — adopted-and-cited, or reference-only-and-why.

## Revising an ADR later (additive history)

See `.claude/protocols/additive-history-editing.md` for the full rule
— summary: strike through (`~~...~~`) what changed, add "Superseded by
`ADR-XXX`" or "Corrected, this session" inline, never delete the
original text outright. The one exception: content written and never
shipped/built within the *same* integration effort may get a clean
rewrite in place instead of a strikethrough — use judgment, and say
which you did.
