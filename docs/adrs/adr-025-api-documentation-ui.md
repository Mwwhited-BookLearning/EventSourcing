[← ADR index](../07-adrs.md)

# ADR-025: API documentation UI — Scalar for OpenAPI, `@asyncapi/react-component` for AsyncAPI

Status: Accepted

Context: `/openapi.json` and `/asyncapi.json` (`ADR-002`) are generated,
anonymous, machine-readable contract documents — nothing renders them for
a human to browse. Both ecosystems have a standard answer for this
(the way Swagger UI is the default OpenAPI renderer), and this design's
own convention throughout (`references.md`) is to adopt the standard
answer rather than hand-roll a viewer.

Decision:
- **OpenAPI (Publish side): [Scalar](../libraries/dotnet/scalar.md)**,
  via the `Scalar.AspNetCore` NuGet package — a modern Swagger-UI
  alternative with an integrated try-it-out client. Minimal wiring,
  reading directly off the existing `/openapi.json` endpoint:
  ```csharp
  app.MapScalarApiReference(); // serves the UI at /scalar
  ```
  No second OpenAPI generation path — Scalar renders whatever
  `OpenApiDocumentBuilder` (`ADR-002`) already produces; it's a pure
  presentation layer on top of `/openapi.json`, not an alternative to it.
- **AsyncAPI (Follow side): [`@asyncapi/react-component`](../libraries/web/asyncapi-react.md)**
  — the foundational rendering library the AsyncAPI org's own
  `html-template` and Studio are themselves built on. There is no
  AsyncAPI-equivalent of a NuGet package (no .NET-native renderer exists);
  the component is a JS bundle, loaded via CDN into a small static page —
  the same "single HTML file" simplicity Scalar itself uses for
  non-.NET stacks — served at a `/asyncapi-ui` route by
  `EventStore.Host.Core`, pointed at the existing `/asyncapi.json`
  endpoint. AsyncAPI Studio (the hosted, editable design tool) is *not*
  used here — this design's `/asyncapi.json` is generated output to
  browse, not a spec to hand-author, so the lighter read-only renderer is
  the correct fit, not the heavier design tool.
- Both UI routes are anonymous, same as the JSON documents they render
  (`ADR-002`/`ADR-006`) — they expose contract shape only, never event
  data.

Consequences:
- `Scalar.AspNetCore` is a genuine new NuGet dependency, but a thin one
  (presentation only) — no change to how `/openapi.json` itself is built.
- The AsyncAPI side has no equivalent first-party .NET package to lean on
  — the static-page-plus-CDN-bundle approach is a deliberate, minimal
  choice given that gap, consistent with how `AsyncApiDocumentBuilder`
  (`ADR-002`) already accepts there's no mature .NET AsyncAPI tooling and
  hand-builds around that gap rather than forcing a workaround.
- Both routes are pure convenience for a human — nothing in the system's
  actual contract (`ADR-002`'s generation, `ADR-013`'s error shape, any
  client integration) depends on either UI existing; removing either
  would not change the API surface at all.
