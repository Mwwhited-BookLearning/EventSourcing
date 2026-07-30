# Template: comparison doc (`docs/comparisons/{slug}.md`)

A comparison is written **before** the deciding ADR, for a genuine
multi-option fork — not after, to justify a choice already made. Real
example: `docs/comparisons/multi-tenant-isolation-model.md`.

## Skeleton

```
[← Comparisons index](README.md)

# {The fork, stated as a question}

**Raised by:** <what surfaced this as a real, unweighed fork — an open
question, an external cross-reference, a direct request>

## The fork
<state each real option plainly, no thumb on the scale yet>

### Option A — {name}
| | |
|---|---|
| **Pros** | ... |
| **Cons** | ... |

### Option B — {name}
(same shape)

## Recommendation
<the actual call, argued from the pros/cons above — not restated from
scratch. Name the real trade-off being accepted, don't hide it.>
```

Then: write the deciding ADR, which cites this comparison rather than
re-deriving the trade-off inline. Add a row to
`docs/comparisons/README.md`'s catalog.

## Rules

- **Verify real-world terminology for each option before naming it** —
  WebFetch the actual source (AWS/Azure docs, a standards body, a
  library's own README), don't recall from memory. Real examples this
  session: "silo model" (AWS Well-Architected SaaS Lens) vs. "pool
  model" (Azure Architecture Center) — neither name was guessed.
- **Widen beyond the two options the requester named, if real
  alternatives exist** — `docs/comparisons/api-query-layer.md` and
  `docs/comparisons/proving-ground-domain.md` both did this on
  purpose, per direct request, and it's good practice generally: check
  the full option space, not just the two most obvious choices.
- **A comparison can also resolve an already-open question without a
  new ADR number**, when the answer just fills in gaps an existing ADR
  already left open (e.g. `docs/comparisons/federated-identity-
  mapping.md`) — note this explicitly in the "Raised by"/decided-in
  framing rather than forcing a new ADR that doesn't add anything.
