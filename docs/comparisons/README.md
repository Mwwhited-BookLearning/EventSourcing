# Decision Comparisons

A fourth kind of document, alongside `docs/adrs/` (a decision made),
`docs/patterns/` (a general pattern explained), and `references.md` (a
bibliography line): **when a real fork has genuine options on more than
one side, write both sides out in full — pros, cons, who picked what and
why in the wild — before the deciding ADR gets written, not after.** An
ADR's "Context" section states a decision's reasoning; a comparison here
is deliberately more exhaustive and more neutral, written *before*
committing, so the choice is made with the full trade-off in view rather
than reasoned-backward to justify whatever was picked first.

## Open forks (gate a not-yet-written ADR)

| Comparison | Gates | Status |
|---|---|---|
| [Sharding strategy](sharding-strategy.md) — entity-type-based vs. hash-based consistent hashing | the queued sharding ADR | Written |
| [Peer-sync topology](peer-sync-topology.md) — gossip/full-mesh vs. hub-and-spoke vs. leaderless pull | the queued replication ADR | Written |
| [Authority rejection behavior](authority-rejection-behavior.md) — annotate-only vs. compensating-patch | the queued non-authoritative-capture ADR | Written |

## Already-decided forks, catalogued for their teaching value

Real alternatives that got a full pros/cons treatment during the
conversation that produced the deciding ADR, but not yet written up as
their own standalone comparison doc — listed here so the catalog is
honest about what's covered vs. what's just recorded inline in an ADR.

| Comparison | Decided in | Note |
|---|---|---|
| Upcast/downcast transform language: OData `compute()` vs. JSONata vs. JMESPath vs. JOLT vs. design-docs' JS+CEL | `ADR-018` | `references.md` records the shortlist and why each lost; not yet pulled into a dedicated side-by-side here |

## Written, decision already made — kept for teaching value beyond the two options actually compared

| Comparison | Decided in | Note |
|---|---|---|
| [API query layer](api-query-layer.md) — GraphQL vs. OData vs. JSON:API vs. gRPC vs. REST-ad-hoc/PostgREST-style | `ADR-037` (GraphQL) | Expanded beyond the two options this project actually chose between (GraphQL, OData), per direct request, to check that choice against every other commonly-named API query surface |
| [UI architecture](ui-architecture-patterns.md) — MVVM vs. MVP vs. MVC vs. code-behind | `ADR-039` (MVVM) | Written as an explicit fallback priority (MVVM → MVP → MVC → code-behind) for UI technologies/screens `ADR-039` doesn't fully dictate, per direct request |
