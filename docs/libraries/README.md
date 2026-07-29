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

Two real gaps this pass found and closed, rather than left as an
abstract "GraphQL Gateway"/"WebDAV surface" with no concrete library
named: [HotChocolate](dotnet/hotchocolate.md) (`ADR-037`'s GraphQL
server) and [NWebDav](dotnet/nwebdav.md) (`ADR-032`'s WebDAV surface).

## Catalog

| Library | Platform | For | Adopted in |
|---|---|---|---|
| [OpenIddict](dotnet/openiddict.md) | dotnet | OAuth2/OIDC token issuance, dev-mode IdP | `ADR-006` |
| [.NET Aspire](dotnet/aspire.md) | dotnet | Local orchestration, service discovery, OpenTelemetry wiring | `ADR-006`, `ADR-026` |
| [EF Core](dotnet/efcore.md) | dotnet | ORM / data access | `02-data-model.md`, `06-solution-structure.md` |
| [HotChocolate](dotnet/hotchocolate.md) | dotnet | GraphQL server (schema, resolvers, Subscriptions, DataLoader) | `ADR-037` |
| [Jint](dotnet/jint.md) | dotnet | Sandboxed JS execution for complex upcast mappings | `ADR-018`, `ADR-037` |
| [CEL for .NET](dotnet/cel-dotnet.md) | dotnet | Declarative expression evaluation for common upcast mappings | `ADR-018`, `ADR-037` (candidates only — ecosystem immature, not locked in) |
| [Testcontainers](dotnet/testcontainers.md) | dotnet | Disposable real-database integration tests | `06-solution-structure.md` |
| [Scalar](dotnet/scalar.md) | dotnet | OpenAPI documentation UI | `ADR-025` |
| [YARP](dotnet/yarp.md) | dotnet | Reverse proxy / API Gateway — single external entry point | `ADR-049` |
| [Microsoft.Extensions.Compliance.Redaction](dotnet/compliance-redaction.md) | dotnet | Data classification + automatic log redaction (PII/PHI/PCI) | `ADR-050` |
| [Vue 3](web/vue.md) | web | Client application shell (MVVM presentation layer) | `ADR-039`, `mvvm-client-architecture.md` |
| [Pinia](web/pinia.md) | web | Client-side state store (MVVM data layer) | `mvvm-client-architecture.md` |
| [Naive UI](web/naive-ui.md) | web | Vue component library + theming | `mvvm-client-architecture.md` |
| [AsyncAPI React component](web/asyncapi-react.md) | web | AsyncAPI documentation UI | `ADR-025` |

## Compared, not adopted

- **WebDAV, entirely** ([NWebDav](dotnet/nwebdav.md), [Dav.AspNetCore.Server](dotnet/dav-aspnetcore-server.md), IT Hit WebDAV Server Engine) — `ADR-032` decided to skip WebDAV outright rather than adopt any of the three; see [the comparison](../comparisons/webdav-library.md). The attachment store's actual access paths (upload, fetch+range, browse/list) are served by plain HTTP and GraphQL instead.

Real alternatives weighed in full in `docs/comparisons/` — recorded
there, not duplicated here, since a library that wasn't picked doesn't
get a "how this design uses it" section: [Blazor, React, Angular](../comparisons/ui-framework.md)
(lost to Vue), and every non-GraphQL option in
[the API query layer comparison](../comparisons/api-query-layer.md).
