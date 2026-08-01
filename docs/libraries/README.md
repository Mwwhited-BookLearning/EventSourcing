# Adopted Libraries & Frameworks

A **fifth kind of document**, alongside `docs/adrs/` (a decision),
`docs/patterns/` (a general pattern explained), `docs/comparisons/`
(a fork weighed in full), and `references.md` (a bibliography line):
one file per concrete, off-the-shelf library or framework this design
adopts — what it's for, plus general usage examples, grouped by
platform folder. Not a copy of that library's own docs; just enough to
orient a reader and show the shape of how this design calls it.
Referenced from whichever ADR/pattern doc actually adopts the library,
rather than repeating usage examples inline there.

## Why this folder exists: buy over build

**Prefer an existing, well-adopted framework/library over a bespoke
mechanism for any complex pattern or task** — the same instinct
`references.md`/`CLAUDE.md` already state for RFCs and specs
("never invent a bespoke mechanism when a real standard already fits"),
extended from specifications to concrete, installable libraries. When a
real gap exists and no library fits — or the gap is genuinely this
project's own business logic — build a small, generalized library
isolated from business logic rather than scattering the logic through
it; that library earns a writeup here too, once it exists.

Two real gaps this pass found and closed at the time, rather than left
as an abstract "GraphQL Gateway"/"WebDAV surface" with no concrete
library named: [HotChocolate](dotnet/hotchocolate.md) (`ADR-037`'s
GraphQL server — still adopted) and [NWebDav](dotnet/nwebdav.md)
(`ADR-032`'s then-proposed WebDAV surface — since declined outright, see
"Compared, not adopted" below).

## This catalog doubles as a SOUP list (`ADR-074`)

Every entry below is, by [IEC 62304](https://openregulatory.com/document_templates/soup-list-software-of-unknown-provenance)'s
own definition, **Software of Unknown Provenance** — off-the-shelf
software not developed specifically for this framework — the moment
this design or a derivative is used in a medical-device software
context (a real concern given the clinical-trials-plus-device-telemetry
proving-ground domain, not hypothetical). `ADR-074` formalizes this
catalog as that SOUP list rather than standing up a parallel document.
**Retrofitted with the two remaining IEC-62304 fields, this session** —
"Known anomalies" (a publicly known limitation/caveat relevant to *how
this design specifically uses the library*, not a general bug-tracker
dump) and "Fulfills" (the specific functional requirement, in this
design's own terms, the library satisfies) — completing what this
catalog needs to actually function as a SOUP list, not just a name/
purpose table. "None known" is itself a real, defensible SOUP-list
entry for a mature, narrowly-used library — IEC 62304 asks what's
*actually known*, not that every entry name a problem. A separate,
automatically-generated **SBOM** ([`microsoft/sbom-tool`](dotnet/sbom-tool.md), SPDX
format) is the machine-readable complement to this human-curated
catalog — the SBOM answers "what's actually built," this catalog
answers "why, and what's the risk."

## Catalog

| Library | Platform | For | Known anomalies | Fulfills | Adopted in |
|---|---|---|---|---|---|
| [OpenIddict](dotnet/openiddict.md) | dotnet | OAuth2/OIDC token issuance, dev-mode IdP | None known affecting dev-mode-IdP usage as scoped here | Token issuance/validation for every authenticated actor | `ADR-006` |
| [.NET Aspire](dotnet/aspire.md) | dotnet | Local orchestration, service discovery, OpenTelemetry wiring | Dev/orchestration tooling only — never a production runtime dependency (`ADR-026`), so a dev-tooling defect can't reach a deployed system | Local multi-service orchestration + telemetry wiring | `ADR-006`, `ADR-026` |
| [EF Core](dotnet/efcore.md) | dotnet | ORM / data access | Well-known general footgun (not specific to this design): naive LINQ composition can produce N+1 query patterns if a developer isn't deliberate — mitigated here by this design's explicit, hand-reviewed query shapes (`ADR-041`'s anti-convention-magic posture), not by an EF Core setting | Portable, provider-abstracted persistence across three database engines (`ADR-001`) | `02-data-model.md`, `06-solution-structure.md` |
| [HotChocolate](dotnet/hotchocolate.md) | dotnet | GraphQL server (schema, resolvers, Subscriptions, DataLoader) | None known affecting this design's usage; depth/cost limiting is explicitly configured, not left at defaults (`ADR-037`) | The sole GraphQL query/mutation/subscription surface | `ADR-037` |
| [Jint](dotnet/jint.md) | dotnet | Sandboxed JS execution for complex upcast mappings | Sandboxing limits resource consumption (loops/memory) but is not a hermetic security boundary against a malicious script — acceptable here because upcast scripts are author-registered schema-registry content, not untrusted third-party input | Complex, imperative upcast logic beyond CEL's declarative expressiveness | `ADR-018`, `ADR-037` |
| [CEL for .NET](dotnet/cel-dotnet.md) | dotnet | Declarative expression evaluation for common upcast mappings — the default `IUpcastExpressionEvaluator` | The .NET port ecosystem is fragmented (several small community packages, no single dominant one) — named explicitly in `docs/libraries/dotnet/cel-dotnet.md`, not a silent risk | The default, declarative upcast-mapping engine | `ADR-018`, `ADR-037`, `ADR-053` |
| [Jsonata.Net.Native](dotnet/jsonata-dotnet.md) | dotnet | Alternative `IUpcastExpressionEvaluator` — array-aggregation upcasts CEL can't express natively | None known affecting this design's narrow, alternative-engine usage | Array-aggregation upcast expressions CEL can't express | `ADR-053` |
| [Testcontainers](dotnet/testcontainers.md) | dotnet | Disposable real-database integration tests | Test-time-only dependency — never ships in a production artifact, so a defect here cannot affect a deployed system | Real-engine (not in-memory-fake) integration test fidelity | `06-solution-structure.md` |
| [Scalar](dotnet/scalar.md) | dotnet | OpenAPI documentation UI | None known affecting this design's usage | Human-readable OpenAPI documentation UI | `ADR-025` |
| [YARP](dotnet/yarp.md) | dotnet | Reverse proxy / API Gateway — single external entry point | None known affecting this design's usage; Microsoft-maintained, used at Microsoft's own production scale | Single external entry point; the natural WAF/perimeter-defense attachment point (`ADR-092`) | `ADR-049` |
| [SPIFFE/SPIRE](dotnet/spiffe-spire.md) | dotnet | Internal service/peer workload identity (X.509-SVID, mTLS, cross-trust-domain federation) | Real operational complexity in cross-trust-domain federation setup, well-documented upstream — accepted given the multi-site/multi-tenant identity need it solves | Verifiable service-to-service and peer-sync workload identity | `ADR-048` |
| [Microsoft.Extensions.Compliance.Redaction](dotnet/compliance-redaction.md) | dotnet | Data classification + automatic log redaction (PII/PHI/PCI) | None known affecting this design's usage | Automatic PII/PHI/PCI redaction in logs, reusing `ADR-050`'s classification metadata | `ADR-050` |
| [Kiota](dotnet/kiota.md) | dotnet (generates C#+TypeScript) | OpenAPI-based client SDK generation — publish-side, one tool for both target languages | None known affecting this design's usage | Generated, type-safe client SDKs from the OpenAPI contract | `ADR-054` |
| [Strawberry Shake](dotnet/strawberry-shake.md) | dotnet | GraphQL client SDK generation for .NET consumers, same vendor as server-side `HotChocolate` | None known affecting this design's usage | Generated, type-safe .NET GraphQL client SDKs | `ADR-054` |
| [MSTest](dotnet/mstest.md) | dotnet | Unit-test framework (backend), also the base classes `Playwright` E2E tests use | Test-time-only dependency — never ships in a production artifact | Backend unit/E2E test execution framework | `ADR-055` |
| [Moq](dotnet/moq.md) | dotnet | Mocking library for unit tests | Test-time-only dependency — never ships in a production artifact | Test-double/mock generation for unit isolation | `ADR-055` |
| [Playwright for .NET](dotnet/playwright-dotnet.md) | dotnet | Cross-browser end-to-end UI action tests | Test-time-only dependency — never ships in a production artifact | Cross-browser E2E UI test execution | `ADR-055` |
| [ASP.NET Core Rate Limiting middleware](dotnet/aspnetcore-ratelimiting.md) | dotnet | Per-tenant rate limiting/quota, first-party, composes with YARP | None known affecting this design's usage; first-party, part of the shared framework since .NET 7 | Per-tenant volume fairness (`ADR-058`) — explicitly not perimeter/security defense (`ADR-092`) | `ADR-058` |
| [FsCheck](dotnet/fscheck.md) | dotnet | Property-based testing — hash-chain and conflict-resolution-policy invariants | Test-time-only dependency — never ships in a production artifact | Property-based verification of `ADR-019`/`ADR-024`'s correctness invariants | `ADR-063` |
| [Polly + Simmy](dotnet/polly-simmy.md) | dotnet | In-process fault injection — durable outbox/inbox crash-recovery testing | Test-time-only dependency — never ships in a production artifact | Simulated-crash verification of `ADR-033`'s outbox/inbox resumption | `ADR-063` |
| [Azure Key Vault](dotnet/azure-key-vault.md) | dotnet | `IErasureKeyStore` backend — cloud key management | None known affecting this design's narrow, wrap/destroy-key usage pattern | Cloud-hosted DEK wrapping/destruction for crypto-shredding erasure | `ADR-057` |
| [AWS KMS](dotnet/aws-kms.md) | dotnet | `IErasureKeyStore` backend — cloud key management | None known affecting this design's narrow, wrap/destroy-key usage pattern | Cloud-hosted DEK wrapping/destruction for crypto-shredding erasure | `ADR-057` |
| [Google Cloud KMS](dotnet/google-cloud-kms.md) | dotnet | `IErasureKeyStore` backend — cloud key management | None known affecting this design's narrow, wrap/destroy-key usage pattern | Cloud-hosted DEK wrapping/destruction for crypto-shredding erasure | `ADR-057` |
| [HashiCorp Vault](dotnet/hashicorp-vault.md) | dotnet | `IErasureKeyStore` backend — on-prem/self-hosted key management | Operator-managed unsealing/HA topology is real operational overhead, well-documented upstream — accepted given the self-hosted requirement it uniquely satisfies among the four backends | On-prem/self-hosted DEK wrapping/destruction, for deployments that can't use a cloud KMS | `ADR-057` |
| [microsoft/sbom-tool](dotnet/sbom-tool.md) | dotnet | Automated SPDX SBOM generation at build/release time | None known affecting this design's usage | Machine-readable SBOM generation (`ADR-074`) | `ADR-074` |
| [BenchmarkDotNet](dotnet/benchmarkdotnet.md) | dotnet | Micro-benchmarking with baseline comparison — performance-regression detection | Test-time-only dependency — never ships in a production artifact | Regression detection on hot paths (fold, hash-chain, `IJsonPathTranslator`) | `ADR-085` |
| [Vue 3](web/vue.md) | web | Client application shell (MVVM presentation layer) | None known affecting this design's usage | The MVVM presentation layer's rendering/reactivity engine | `ADR-039`, `mvvm-client-architecture.md` |
| [Pinia](web/pinia.md) | web | Client-side state store (MVVM data layer) | None known affecting this design's usage | Client-side ViewModel state management | `mvvm-client-architecture.md` |
| [Naive UI](web/naive-ui.md) | web | Vue component library + theming | None known affecting this design's usage | Baseline component/theming layer for entity views | `mvvm-client-architecture.md` |
| [AsyncAPI React component](web/asyncapi-react.md) | web | AsyncAPI documentation UI | None known affecting this design's usage | Human-readable AsyncAPI documentation UI | `ADR-025` |
| [GraphQL Code Generator](web/graphql-code-generator.md) | web | GraphQL client SDK generation for TypeScript consumers | None known affecting this design's usage | Generated, type-safe TypeScript GraphQL client SDKs | `ADR-054` |
| [Vitest](web/vitest.md) | web | Unit-test runner (frontend), Vite-native | Test-time-only dependency — never ships in a production artifact | Frontend unit-test execution | `ADR-055` |
| [Vue Test Utils](web/vue-test-utils.md) | web | Vue's own component-mounting/testing library | Test-time-only dependency — never ships in a production artifact | Vue component-level test mounting/interaction | `ADR-055` |
| [vite-plugin-singlefile](web/vite-plugin-singlefile.md) | web | Inlines a build into one static HTML file — the offline litigation-review player | None known affecting this design's narrow, single-artifact-bundling usage | Single-file, dependency-free offline player artifact (`ADR-068`) | `ADR-068` |

## Compared, not adopted

- **WebDAV, entirely** ([NWebDav](dotnet/nwebdav.md), [Dav.AspNetCore.Server](dotnet/dav-aspnetcore-server.md), IT Hit WebDAV Server Engine) — `ADR-032` decided to skip WebDAV outright rather than adopt any of the three; see [the comparison](../comparisons/webdav-library.md). The attachment store's actual access paths (upload, fetch+range, browse/list) are served by plain HTTP and GraphQL instead.

Real alternatives weighed in full in `docs/comparisons/` — recorded
there, not duplicated here, since a library that wasn't picked doesn't
get a "how this design uses it" section: [Blazor, React, Angular](../comparisons/ui-framework.md)
(lost to Vue), and every non-GraphQL option in
[the API query layer comparison](../comparisons/api-query-layer.md).

## Named for a future escalation, not yet adopted

`ADR-063`'s staged distributed-correctness testing path names two further
tools as the deliberate, deferred next steps if this design ever moves
toward production — not adopted now, so deliberately given no standalone
writeup here (there is no "how this design calls it" yet): **Testcontainers
+ [Toxiproxy](https://github.com/Shopify/toxiproxy)** (real network-level
fault injection — genuine multi-process partition testing, reusing the
`Testcontainers` infrastructure already adopted above) as the first move
once a real production deployment is being planned, and **Jepsen-style
external black-box verification** as the named ceiling beyond that. See
[`docs/comparisons/distributed-correctness-testing.md`](../comparisons/distributed-correctness-testing.md)
for the full comparison and `ADR-063` for the staged-adoption decision.

`ADR-085`'s staged performance-testing path names one further tool the
same way: **[NBomber](https://nbomber.com/)** (a .NET-native, protocol-
agnostic load-testing framework) as the named first move for real end-
to-end load/soak testing once an actual deployment target exists to
test against — not adopted now, alongside `BenchmarkDotNet` above, for
the same reason: there's no running deployment yet to load-test.
