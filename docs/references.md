# References

A single bibliography for every real-world standard, spec, pattern, or
library this design package relies on or seriously considered. Each
numbered doc (`01`–`09`) also carries its own short "Suggested References"
section with just the citations relevant to that doc — this file is the
comprehensive index, organized by whether the concept was actually
**adopted** into the design or is recorded here as **reference-only**
(considered, and explicitly not built, with the reason stated).

## Adopted — used directly in this design

| Concept | Standard / spec | Where it's used |
|---|---|---|
| OAuth2 Client Credentials grant | [RFC 6749 §4.4](https://datatracker.ietf.org/doc/html/rfc6749#section-4.4) — The OAuth 2.0 Authorization Framework | `ADR-006` |
| Bearer token usage | [RFC 6750](https://datatracker.ietf.org/doc/html/rfc6750) — OAuth 2.0 Bearer Token Usage | `ADR-006` |
| Demonstrating Proof of Possession | [RFC 9449](https://datatracker.ietf.org/doc/html/rfc9449) — OAuth 2.0 DPoP | `ADR-017` |
| OIDC discovery | [OpenID Connect Discovery 1.0](https://openid.net/specs/openid-connect-discovery-1_0.html) — OpenID Foundation | `ADR-006` (`/.well-known/openid-configuration`) |
| HTTP QUERY method | [RFC 10008](https://datatracker.ietf.org/doc/html/rfc10008) — The HTTP QUERY Method (June 2026) | `ADR-012` |
| Problem Details for HTTP APIs | [RFC 9457](https://datatracker.ietf.org/doc/html/rfc9457) (obsoletes RFC 7807) | `ADR-013` |
| CORS | [WHATWG Fetch Standard](https://fetch.spec.whatwg.org/#http-cors-protocol) — Cross-Origin Resource Sharing protocol | `ADR-014` |
| Server-Sent Events | [WHATWG HTML — Server-Sent Events](https://html.spec.whatwg.org/multipage/server-sent-events.html) | `03-api-contracts.md`, `features/follow-subscribe.md` |
| JSON | [RFC 8259](https://datatracker.ietf.org/doc/html/rfc8259) — The JavaScript Object Notation (JSON) Data Interchange Format | throughout (`Payload`, `JsonSchema` storage) |
| JSON Schema (2020-12) | [json-schema.org](https://json-schema.org/specification) | `02-data-model.md`, `05-schema-registry-and-spec-generation.md` |
| JSON Merge Patch | [RFC 7396](https://datatracker.ietf.org/doc/html/rfc7396) | `ADR-016`'s `SnapshotMerger` (overwrite-if-present half only — see `ADR-016`'s closing note on why the delete-on-`null` half is deliberately not used) |
| JSONPath-style path expressions | [RFC 9535](https://datatracker.ietf.org/doc/html/rfc9535) — JSONPath | `FilterableField.JsonPath` (`$.Amount` syntax), `04-odata-filter-pushdown.md` |
| OData `$filter`/`$top`/`$skip` syntax (borrowed, not full spec compliance) | [OASIS OData v4.01 — URL Conventions](https://docs.oasis-open.org/odata/odata/v4.01/odata-v4.01-part2-url-conventions.html) | `04-odata-filter-pushdown.md`, `ADR-003`, `ADR-012` |
| OpenAPI 3.1 | [OpenAPI Specification v3.1.1](https://spec.openapis.org/oas/v3.1.1.html) | `ADR-002`, `03-api-contracts.md`, via the `Microsoft.OpenApi` library |
| AsyncAPI 3.0 | [AsyncAPI Specification v3.0](https://www.asyncapi.com/docs/reference/specification/v3.0.0) | `ADR-002`, `03-api-contracts.md` |
| SHA-256 | [FIPS 180-4](https://csrc.nist.gov/pubs/fips/180-4/final) — Secure Hash Standard | `ADR-011` (`PayloadHash`), `ADR-019` (`ChainHash`) |
| Verifiable/tamper-evident append-only logs (model, not the binary-tree mechanics) | [RFC 9162](https://datatracker.ietf.org/doc/html/rfc9162) — Certificate Transparency v2.0 | `ADR-019` (explicitly a simpler *linear* chain, not CT's Merkle tree — see `ADR-019`'s Decision for why) |
| Event upcaster chains (the chaining *concept*, not this design's expression language) | [Axon Framework — Event Versioning](https://docs.axoniq.io/axon-framework-reference/4.11/events/event-versioning/) | `ADR-018`'s `UpcastChain` shape |
| Upcast mapping expressions | [OASIS OData Data Aggregation Extension v4.0 — `compute()`](https://docs.oasis-open.org/odata/odata-data-aggregation-ext/v4.0/odata-data-aggregation-ext-v4.0.html) | `ADR-018`'s `upcastFromPrevious` field — chosen over a general transform language (JSONata/JMESPath, see below) because an upcast mapping is always many-to-one/one-to-one, and reusing `Microsoft.OData.UriParser` (already a dependency for `$filter`) avoids a second expression grammar |
| C4 model | [c4model.com](https://c4model.com/) — Simon Brown | `01-c4-architecture.md` |
| C4-PlantUML | [plantuml-stdlib/C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) | `01-c4-architecture.md` |
| Gherkin / BDD | [Cucumber — Gherkin Reference](https://cucumber.io/docs/gherkin/reference/) | every `features/*.md` |
| EF Core | [Microsoft Learn — EF Core](https://learn.microsoft.com/en-us/ef/core/) | `02-data-model.md`, `06-solution-structure.md` |
| OpenIddict | [openiddict.com](https://openiddict.com/) | `ADR-006`, `EventStore.DevIdp` |
| .NET Aspire | [Microsoft Learn — .NET Aspire overview](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview) | `ADR-006`, `EventStore.AppHost` |
| Testcontainers | [testcontainers.com](https://testcontainers.com/) | `06-solution-structure.md`, integration test strategy |
| Event Sourcing / CQRS (the general pattern) | [Martin Fowler — Event Sourcing](https://martinfowler.com/eaaDev/EventSourcing.html) / [CQRS](https://martinfowler.com/bliki/CQRS.html) | `09-cqrs-read-models.md`, `ADR-015`/`ADR-016` |
| Scalar | [scalar/scalar](https://github.com/scalar/scalar), `Scalar.AspNetCore` NuGet package | `ADR-025` — OpenAPI docs UI |
| AsyncAPI React component | [asyncapi/asyncapi-react](https://github.com/asyncapi/asyncapi-react) | `ADR-025` — AsyncAPI docs UI |
| OpenTelemetry | [opentelemetry.io](https://opentelemetry.io/), via .NET Aspire's `ServiceDefaults` | `ADR-026` — logging, tracing, and metrics for every service |
| Docker Compose | [docs.docker.com/compose](https://docs.docker.com/compose/) | `ADR-026` — production deployment path |

## Reference-only — considered, not adopted

Recorded here for the same reason `README.md` states its own "deliberately
is not" scope decisions plainly rather than silently: so a reader doesn't
have to wonder whether something was overlooked versus deliberately left
out.

| Concept | Standard / spec | Why it's not in this design |
|---|---|---|
| Transactional Outbox pattern | [microservices.io — Transactional Outbox](https://microservices.io/patterns/data/transactional-outbox.html) | This design never needed it. The classic outbox problem is "how do I atomically update my own row *and* publish an event about it" (the dual-write problem). Here, `Events` **is** the log Follow tails directly — publish and append are the same write, so there's no second system to keep in sync with (`ADR-015`'s consequences touch on this same point from the projection side). |
| OAuth 2.0 Token Exchange | [RFC 8693](https://datatracker.ietf.org/doc/html/rfc8693) | Every actor in this design (including `ProjectionHost`, `ADR-015`) is a static, independently-seeded OAuth2 client with its own identity — none of them need to act "on behalf of" another identity today. Would become directly relevant if `ADR-007`'s deferred derived-event-type worker needs to republish events while preserving an upstream identity in an `act` claim; not designed further than this note. |
| SPIFFE/SPIRE workload identity | [spiffe.io](https://spiffe.io/docs/latest/spiffe-about/overview/), [SPIRE](https://spiffe.io/docs/latest/spire-about/spire-concepts/) | This system has no multi-workload mesh needing cross-platform workload attestation — a handful of statically-seeded OAuth2 clients (`ADR-006`) already covers the actual service-to-service auth surface at this project's scale. Worth revisiting only if this ever grows into a real multi-service mesh. |
| YARP / reverse-proxy forwarding | [dotnet/yarp](https://github.com/dotnet/yarp) | No gateway/reverse-proxy tier exists in this design — each `EventStore.Host.<Provider>` (`ADR-001`) is a single deployable serving its own endpoints directly. |
| DNS-SD / mDNS service discovery | [RFC 6763](https://datatracker.ietf.org/doc/html/rfc6763), [RFC 6762](https://datatracker.ietf.org/doc/html/rfc6762) | No multi-instance discovery problem exists here — orchestration (Aspire service discovery, or `docker-compose`'s DNS, `ADR-006`) already solves connecting the store, its database, and the dev IdP at this project's scale. |
| Strangler Fig migration pattern | [Martin Fowler — StranglerFigApplication](https://martinfowler.com/bliki/StranglerFigApplication.html) | This is a from-scratch design, not a legacy migration — there's nothing being strangled. |
| Content-addressable storage | [Git Internals — Git Objects](https://git-scm.com/book/en/v2/Git-Internals-Git-Objects), [IPFS whitepaper](https://arxiv.org/abs/1407.3561) | `Payload` is small JSON, not large blobs — there's no large-object storage problem here for content-addressing to solve. (`ADR-019`'s hash chain solves a *different* problem — tamper evidence across history — with the same hash primitive, not object storage.) |
| Capability tokens (Macaroons/Biscuit) | [Macaroons (Google/NDSS 2014)](https://research.google/pubs/macaroons-cookies-with-contextual-caveats-for-decentralized-authorization-in-the-cloud/), [Biscuit](https://doc.biscuitsec.org/reference/specifications.html) | `ADR-008`'s static `RequiredPublishClaim`/`RequiredReadClaim` are sufficient for this design's claim model (one required claim per direction, checked against an already-issued JWT). Attenuating, offline-verifiable delegation would only earn its complexity if a caller needed to *mint* a narrower credential for someone else without going back to the IdP — no actor in this design does that. |
| UCAN | [ucan.xyz](https://ucan.xyz/specification/) | Same reasoning as capability tokens above — no actor needs offline, authority-free credential attenuation. |
| W3C DIDs / Verifiable Credentials | [DIDs v1.1](https://www.w3.org/TR/did-1.1/), [VC Data Model v2.0](https://www.w3.org/TR/vc-data-model-2.0/) | All four actors are machine clients with static OAuth2 credentials (`ADR-006`) — there's no decentralized-identity or user-facing credential-issuance problem here for DIDs/VCs to solve. |
| Local-first software / CRDTs | [Ink & Switch — Local-first software](https://www.inkandswitch.com/essay/local-first/), [Automerge](https://automerge.org/docs/hello/), [Yjs](https://docs.yjs.dev/) | This is a single logical store with one writer path (`POST /publish`) — there's no concurrent-offline-edit-merge problem; every Follow/projection consumer is read-only relative to the store, so there's nothing to reconcile via a CRDT merge function. |
| Chain-of-Responsibility (rendering fallback) | *Design Patterns* (Gamma, Helm, Johnson, Vlissides), 1994 | This design has no UI/rendering layer — every feature doc that mentions a Salt mockup explicitly states "not applicable" for exactly this reason. |
| Off-the-shelf event-store products | [EventStoreDB](https://www.eventstore.com/eventstoredb), [Marten](https://martendb.io/) (.NET/Postgres) | Deliberately not adopted — building the write/read mechanism from scratch, end-to-end, is this project's stated purpose (`README.md`: "a worked example, not just a store"), not a gap a real product would fill. Worth knowing about if this pattern is ever needed for production rather than as a teaching example. |
| JSONata (general JSON-to-JSON transform language) | [docs.jsonata.org](https://docs.jsonata.org/overview.html) | Seriously considered for `ADR-018`'s upcast mapping — genuinely the most "universal" option (independent implementations in JS/Java/Python/Go/C++/.NET/Rust) and a real fit for "path expressions with functional descriptions." Not adopted because it's strictly more general than the problem: an upcast mapping is always many-to-one/one-to-one (never a restructuring/fan-out), so its object/array-construction and looping constructs would never be exercised — `compute()` already covers the actual need, with no new dependency. Revisit if a future event type genuinely needs a one-to-many/many-to-many reshape across a version bump (`ADR-018`'s closing consequence). |
| JMESPath (JSON query/reshape language) | [jmespath.org](https://jmespath.org/specification.html) | Same consideration as JSONata, one notch narrower (no user-defined functions/closures) — real, multi-language (used across the AWS SDKs), but not adopted for the same "more general than the problem" reason. |
| JOLT (declarative JSON-to-JSON spec) | [bazaarvoice/jolt](https://github.com/bazaarvoice/jolt) | Considered and ruled out — Java-only with no cross-language ports, and structural-only by design (its own docs say to "write code to fix values" for anything computed), which fails the "functional descriptions" requirement outright. |
