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

Every comparison below gated an ADR at the time it was written, and all of
those ADRs are now Accepted — kept here (not archived) because each
weighs real alternatives beyond just the one this project picked, which
is the actual teaching value of this document type. **One exception,
flagged rather than silently included as if it were the same as the
rest:** [Masking content strategies](masking-strategies.md) has a
decided winner for its cheapest increment (`PartialReveal`, `ADR-009`/
`ADR-052`) but explicitly no clear pick for the rest of its menu
(`Hash`, tokenization, generalization) — see its own row below and
`docs/10-open-questions.md`.

| Comparison | Decided in | Note |
|---|---|---|
| [Sharding strategy](sharding-strategy.md) — entity-type-based vs. hash-based consistent hashing | `ADR-034` (entity-type-based) | |
| [Peer-sync topology](peer-sync-topology.md) — gossip/full-mesh vs. hub-and-spoke vs. leaderless pull | `ADR-033` (gossip/full-mesh) | |
| [Authority rejection behavior](authority-rejection-behavior.md) — annotate-only vs. compensating-patch | `ADR-035` (annotate-only default) | |
| [API query layer](api-query-layer.md) — GraphQL vs. OData vs. JSON:API vs. gRPC vs. REST-ad-hoc/PostgREST-style | `ADR-037` (GraphQL) | Expanded beyond the two options this project actually chose between (GraphQL, OData), per direct request, to check that choice against every other commonly-named API query surface |
| [UI architecture](ui-architecture-patterns.md) — MVVM vs. MVP vs. MVC vs. code-behind | `ADR-039` (MVVM) | Written as an explicit fallback priority (MVVM → MVP → MVC → code-behind) for UI technologies/screens `ADR-039` doesn't fully dictate, per direct request |
| [UI framework](ui-framework.md) — Vue vs. Blazor vs. React vs. Angular | `mvvm-client-architecture.md`'s concrete mapping (Vue) | Checks that choice against a genuine first-party alternative (Blazor) under `ADR-041`'s own preference — Blazor loses specifically on `ADR-039`'s runtime-data view-definition mechanism, not by default |
| [Service-to-service identity](service-identity.md) — SPIFFE/SPIRE vs. static OAuth2 client credentials vs. hand-rolled mTLS | `ADR-048` (SPIFFE/SPIRE) | Un-rejects `references.md`'s prior SPIFFE/SPIRE rejection once this design's own growth (many services, `ADR-033`'s cross-site peers) created the multi-workload-mesh scenario the rejection named as the reason to revisit |
| [Peer discovery](peer-discovery.md) — static seed-peer list vs. DNS-based seed discovery vs. dedicated discovery/rendezvous service | `ADR-051` (static seed-peer list, explicit configuration) | Distinct from `peer-sync-topology.md` (how peers sync once connected) and `ADR-048` (how peers authenticate once found) — this is how a newly-deployed peer (`ADR-033`) finds its first live contact at all, outside any shared orchestration boundary |
| [WebDAV library](webdav-library.md) — NWebDav vs. Dav.AspNetCore.Server vs. IT Hit WebDAV Server Engine vs. skip | `ADR-032` (skip WebDAV entirely) | NWebDav is archived, `Dav.AspNetCore.Server` is free but thin, the one well-maintained option is commercial — but the deciding fact is that every *other* attachment access path (upload, fetch+range, browse/list) was already served by mechanisms this design had adopted for unrelated reasons (plain HTTP, GraphQL), so WebDAV's one unique value (OS-native mounting) wasn't worth any of the three trade-offs |
| [Upcast transform language](upcast-transform-language.md) — CEL vs. JSONata vs. JMESPath vs. JOLT vs. OData `compute()` | `ADR-037`/`ADR-053` (CEL default, pluggable) | CEL wins on safety/performance/problem-fit for the common case; rather than force a permanent pick given JSONata's .NET-maturity edge and array-aggregation capability, `ADR-053` makes the engine swappable per deployment |
| [Authority-axis granularity](authority-axis-granularity.md) — collapsed `AuthorityStatus`/`ReaderTrustBasis` vs. independent identity-assurance/content-confidence/reader-assurance axes (NIST 800-63-3 IAL/AAL/FAL) | Reaffirms `ADR-035`/`ADR-042`/`ADR-045` (no new ADR) | Resolves the open question formalizing the pattern doc's "not yet decided" note — concludes the split doesn't change `ADR-042`'s fold outcome in any concrete scenario checked, names explicit triggers for revisiting |
| [Trust-root registration gate](trust-root-registration-gate.md) — narrower `registry:trust-admin` scope vs. `ADR-043`-style delegation vs. dual-control/Four Eyes approval | `ADR-044`'s Consequences (resolved directly, no new ADR) | Resolves the open question `ADR-044` raised; recommends the narrow scope as the mandatory gate, delegation as how it composes for multi-tenant operators, and explicitly declines system-wide dual control — argued as the one action in this design where Four Eyes' precondition is actually met, but disproportionate given `AppTrustRoot`'s per-`AppId`-contained blast radius |
| [Masking content strategies](masking-strategies.md) — configurable partial reveal vs. format-preserving masking vs. generalization/bucketing vs. tokenization | `PartialReveal` decided (`ADR-009`/`ADR-052`); `Hash`/generalization/tokenization not yet | `PartialReveal` (format-preserving, e.g. an SSN rendered `XXX-XX-1234`) fits `ADR-009`'s claims-gated wrapper cleanly and is now built, shared with `ADR-052`'s streaming-channel redaction. Still undecided: `Hash`; generalization fits only as a single-value transform (never a k-anonymity guarantee); tokenization's separate-party reversal model doesn't fit the wrapper at all — it would need its own mechanism, not a `strategy` value |
| [Federated identity mapping](federated-identity-mapping.md) — bare `sub == ActorId` vs. composite (`iss`, `sub`) vs. full JIT provisioning + SCIM-shaped lifecycle | `ADR-047` (Consequences addendum — composite `iss`+`sub` via a new `FederatedIdentityMapping` record, JIT-provisioned at token-exchange time) | Resolves `docs/10-open-questions.md`'s `sub`-to-`ActorId` mapping question directly, per OpenID Connect's own spec guidance (`iss`+`sub` together, never `sub` alone, is the only stable cross-issuer identifier) — no separate ADR written, since it only fills in `ADR-047`'s own already-flagged gap |
| [Streaming-channel redaction mechanism](streaming-redaction-mechanism.md) — zero-fill vs. statistical-noise substitution (`RawScalar`/`RawBinary`), tone vs. silence and a spatial/temporal scope disambiguation (`Media`), materialized (`ADR-027`-style) vs. read-time (`ADR-028`-style) | `ADR-052` (read-time, zero-fill/tone default, configurable `Strategy` incl. `PartialReveal`) | Resolves `ADR-031`'s deliberately-deferred `RedactedRange` transform: searched prior art (SWGDE redaction guidelines, ONVIF `PrivacyMask`, SCTE-35 blackout signaling, differential privacy) found nothing formal that fits the shape directly, and surfaced that most industry tooling solves a spatial (in-frame) redaction problem `RedactedRange`'s field shape doesn't actually support (temporal only) |

## Not yet written up as their own standalone comparison doc

Real alternatives that got a full pros/cons treatment during the
conversation that produced the deciding ADR, but not pulled into their
own file here yet — listed so the catalog stays honest about what's
covered vs. what's just recorded inline in an ADR.

