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
catalog as that SOUP list rather than standing up a parallel document —
it already carries most of what IEC 62304 asks for (name, what it's
for, where it's adopted); known anomalies and the specific functional
requirement each library fulfills are the fields still to be retrofitted
per entry, named as real remaining work, not silently skipped. A
separate, automatically-generated **SBOM** ([`microsoft/sbom-tool`](dotnet/sbom-tool.md), SPDX
format) is the machine-readable complement to this human-curated
catalog — the SBOM answers "what's actually built," this catalog
answers "why, and what's the risk."

## Catalog

| Library | Platform | For | Adopted in |
|---|---|---|---|
| [OpenIddict](dotnet/openiddict.md) | dotnet | OAuth2/OIDC token issuance, dev-mode IdP | `ADR-006` |
| [.NET Aspire](dotnet/aspire.md) | dotnet | Local orchestration, service discovery, OpenTelemetry wiring | `ADR-006`, `ADR-026` |
| [EF Core](dotnet/efcore.md) | dotnet | ORM / data access | `02-data-model.md`, `06-solution-structure.md` |
| [HotChocolate](dotnet/hotchocolate.md) | dotnet | GraphQL server (schema, resolvers, Subscriptions, DataLoader) | `ADR-037` |
| [Jint](dotnet/jint.md) | dotnet | Sandboxed JS execution for complex upcast mappings | `ADR-018`, `ADR-037` |
| [CEL for .NET](dotnet/cel-dotnet.md) | dotnet | Declarative expression evaluation for common upcast mappings — the default `IUpcastExpressionEvaluator` | `ADR-018`, `ADR-037`, `ADR-053` |
| [Jsonata.Net.Native](dotnet/jsonata-dotnet.md) | dotnet | Alternative `IUpcastExpressionEvaluator` — array-aggregation upcasts CEL can't express natively | `ADR-053` |
| [Testcontainers](dotnet/testcontainers.md) | dotnet | Disposable real-database integration tests | `06-solution-structure.md` |
| [Scalar](dotnet/scalar.md) | dotnet | OpenAPI documentation UI | `ADR-025` |
| [YARP](dotnet/yarp.md) | dotnet | Reverse proxy / API Gateway — single external entry point | `ADR-049` |
| [SPIFFE/SPIRE](dotnet/spiffe-spire.md) | dotnet | Internal service/peer workload identity (X.509-SVID, mTLS, cross-trust-domain federation) | `ADR-048` |
| [Microsoft.Extensions.Compliance.Redaction](dotnet/compliance-redaction.md) | dotnet | Data classification + automatic log redaction (PII/PHI/PCI) | `ADR-050` |
| [Kiota](dotnet/kiota.md) | dotnet (generates C#+TypeScript) | OpenAPI-based client SDK generation — publish-side, one tool for both target languages | `ADR-054` |
| [Strawberry Shake](dotnet/strawberry-shake.md) | dotnet | GraphQL client SDK generation for .NET consumers, same vendor as server-side `HotChocolate` | `ADR-054` |
| [MSTest](dotnet/mstest.md) | dotnet | Unit-test framework (backend), also the base classes `Playwright` E2E tests use | `ADR-055` |
| [Moq](dotnet/moq.md) | dotnet | Mocking library for unit tests | `ADR-055` |
| [Playwright for .NET](dotnet/playwright-dotnet.md) | dotnet | Cross-browser end-to-end UI action tests | `ADR-055` |
| [ASP.NET Core Rate Limiting middleware](dotnet/aspnetcore-ratelimiting.md) | dotnet | Per-tenant rate limiting/quota, first-party, composes with YARP | `ADR-058` |
| [FsCheck](dotnet/fscheck.md) | dotnet | Property-based testing — hash-chain and conflict-resolution-policy invariants | `ADR-063` |
| [Polly + Simmy](dotnet/polly-simmy.md) | dotnet | In-process fault injection — durable outbox/inbox crash-recovery testing | `ADR-063` |
| [Azure Key Vault](dotnet/azure-key-vault.md) | dotnet | `IErasureKeyStore` backend — cloud key management | `ADR-057` |
| [AWS KMS](dotnet/aws-kms.md) | dotnet | `IErasureKeyStore` backend — cloud key management | `ADR-057` |
| [Google Cloud KMS](dotnet/google-cloud-kms.md) | dotnet | `IErasureKeyStore` backend — cloud key management | `ADR-057` |
| [HashiCorp Vault](dotnet/hashicorp-vault.md) | dotnet | `IErasureKeyStore` backend — on-prem/self-hosted key management | `ADR-057` |
| [microsoft/sbom-tool](dotnet/sbom-tool.md) | dotnet | Automated SPDX SBOM generation at build/release time | `ADR-074` |
| [Vue 3](web/vue.md) | web | Client application shell (MVVM presentation layer) | `ADR-039`, `mvvm-client-architecture.md` |
| [Pinia](web/pinia.md) | web | Client-side state store (MVVM data layer) | `mvvm-client-architecture.md` |
| [Naive UI](web/naive-ui.md) | web | Vue component library + theming | `mvvm-client-architecture.md` |
| [AsyncAPI React component](web/asyncapi-react.md) | web | AsyncAPI documentation UI | `ADR-025` |
| [GraphQL Code Generator](web/graphql-code-generator.md) | web | GraphQL client SDK generation for TypeScript consumers | `ADR-054` |
| [Vitest](web/vitest.md) | web | Unit-test runner (frontend), Vite-native | `ADR-055` |
| [Vue Test Utils](web/vue-test-utils.md) | web | Vue's own component-mounting/testing library | `ADR-055` |
| [vite-plugin-singlefile](web/vite-plugin-singlefile.md) | web | Inlines a build into one static HTML file — the offline litigation-review player | `ADR-068` |

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
