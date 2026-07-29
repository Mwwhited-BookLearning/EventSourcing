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

## Written, decision already made — kept for teaching value beyond the two (or more) options actually compared

Every comparison below gated an ADR at the time it was written; all of
those ADRs are now Accepted — kept here (not archived) because each
weighs real alternatives beyond just the one this project picked, which
is the actual teaching value of this document type.

| Comparison | Decided in | Note |
|---|---|---|
| [Sharding strategy](sharding-strategy.md) — entity-type-based vs. hash-based consistent hashing | `ADR-034` (entity-type-based) | |
| [Peer-sync topology](peer-sync-topology.md) — gossip/full-mesh vs. hub-and-spoke vs. leaderless pull | `ADR-033` (gossip/full-mesh) | |
| [Authority rejection behavior](authority-rejection-behavior.md) — annotate-only vs. compensating-patch | `ADR-035` (annotate-only default) | |
| [API query layer](api-query-layer.md) — GraphQL vs. OData vs. JSON:API vs. gRPC vs. REST-ad-hoc/PostgREST-style | `ADR-037` (GraphQL) | Expanded beyond the two options this project actually chose between (GraphQL, OData), per direct request, to check that choice against every other commonly-named API query surface |
| [UI architecture](ui-architecture-patterns.md) — MVVM vs. MVP vs. MVC vs. code-behind | `ADR-039` (MVVM) | Written as an explicit fallback priority (MVVM → MVP → MVC → code-behind) for UI technologies/screens `ADR-039` doesn't fully dictate, per direct request |
| [UI framework](ui-framework.md) — Vue vs. Blazor vs. React vs. Angular | `mvvm-client-architecture.md`'s concrete mapping (Vue) | Checks that choice against a genuine first-party alternative (Blazor) under `ADR-041`'s own preference — Blazor loses specifically on `ADR-039`'s runtime-data view-definition mechanism, not by default |
| [Service-to-service identity](service-identity.md) — SPIFFE/SPIRE vs. static OAuth2 client credentials vs. hand-rolled mTLS | `ADR-048` (SPIFFE/SPIRE) | Un-rejects `references.md`'s prior SPIFFE/SPIRE rejection once this design's own growth (many services, `ADR-033`'s cross-site peers) created the multi-workload-mesh scenario the rejection named as the reason to revisit |

## Not yet written up as their own standalone comparison doc

Real alternatives that got a full pros/cons treatment during the
conversation that produced the deciding ADR, but not pulled into their
own file here yet — listed so the catalog stays honest about what's
covered vs. what's just recorded inline in an ADR.

| Comparison | Decided in | Note |
|---|---|---|
| Upcast/downcast transform language: OData `compute()` vs. JSONata vs. JMESPath vs. JOLT vs. this design's JS+CEL | `ADR-018` | `references.md` records the shortlist and why each lost; not yet pulled into a dedicated side-by-side here |
