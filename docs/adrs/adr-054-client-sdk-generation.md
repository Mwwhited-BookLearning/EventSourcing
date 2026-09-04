[← ADR index](../07-adrs.md)

# ADR-054: Client SDK generation — Kiota for OpenAPI, GraphQL Code Generator (TypeScript) + Strawberry Shake (.NET) for GraphQL

Status: Accepted

Context: `docs/10-open-questions.md` asked whether this framework should
name a client-SDK/codegen story at all, given both exposed contracts
(`ADR-002`'s OpenAPI for publish, `ADR-037`'s GraphQL SDL for query/
subscribe) already support off-the-shelf generation. Direction received
this session: generate typed SDKs for **at least .NET and TypeScript**,
resolving the "or neither" branch of that question outright. Searched
prior art before picking tools, per this project's standing convention:

- **OpenAPI side**: `NSwag` and `OpenAPI Generator`/`Swagger Codegen`
  (community/Java-based, 40+ language targets) are both real options,
  but **Kiota** ([microsoft/kiota](https://github.com/microsoft/kiota),
  actively maintained, C#/TypeScript/Java/Go/Python/PHP/Ruby/Swift
  targets from one OpenAPI description) is Microsoft's own first-party
  tool — consistent with `ADR-041`'s "prefer first-party" convention,
  the same reasoning that already picked `Microsoft.Extensions.
  Compliance.Redaction` over a third-party redaction library. One tool
  generates both requested languages (C# and TypeScript) from the same
  spec, rather than two separate community tools with two different
  conventions to track.
- **GraphQL side**: no single tool covers both target languages well —
  the ecosystem splits cleanly by language. **GraphQL Code Generator**
  ([the-guild.dev/graphql/codegen](https://the-guild.dev/graphql/codegen))
  is the de facto standard for TypeScript (typed operations, hooks for
  React/Vue/Angular). **Strawberry Shake**
  ([chillicream.com/docs/strawberryshake](https://chillicream.com/docs/strawberryshake/),
  actively maintained, v16 as of this writing) is ChilliCream's own
  GraphQL client for .NET — the same vendor as `ADR-037`'s server-side
  `HotChocolate`, so schema/SDL conventions (and any ChilliCream-specific
  extensions the server ever adopts) stay in one vendor's hands across
  both ends of the wire, not split across an unrelated client library
  guessing at server behavior.

Decision:
- **Generate, don't hand-write, client SDKs for .NET and TypeScript**,
  from the two contracts this design already publishes anonymously
  (`ADR-002`/`03-api-contracts.md`'s OpenAPI, `ADR-037`'s GraphQL SDL) —
  no new server-side surface, purely a consumer-tooling decision.
- **Kiota** generates the OpenAPI-side (publish) client, for both C#
  and TypeScript, from one `openapi.json`. Run as part of a consuming
  application's own build (CLI or the VS Code extension), not committed
  generated code living in this repository — the framework publishes
  the spec; consumers regenerate against the version they target, the
  same posture `03-api-contracts.md` already takes for the spec itself
  (anonymous, always-current, `GET /openapi.json`).
- **GraphQL Code Generator** generates the GraphQL-side (query/
  subscribe) TypeScript client from `ADR-037`'s SDL; **Strawberry
  Shake** generates the equivalent for .NET consumers. Same
  regenerate-at-consumer-build-time posture as Kiota above — a schema
  change is discovered at the consumer's next build, not silently
  drifted past.
- **No change to `ADR-039`'s Vue/Pinia reference client** — it remains
  the one worked example of consuming this framework end-to-end, not
  itself the "official SDK." An official SDK is the *generated* client
  libraries above; `ADR-039`'s app is illustrative, hand-written
  application code that happens to sit on top of one.
- **Not building a second, hand-maintained SDK on top of the generated
  ones.** Kiota/GraphQL Code Generator/Strawberry Shake's generated
  types and request builders are the SDK — wrapping them in a further,
  hand-written abstraction layer is exactly the kind of speculative
  extra surface `ADR-009`'s KISS-based masking-strategy declines already
  reasoned against; add one only if a real, stated ergonomics gap shows
  up in practice, not preemptively.

Consequences:
- Resolves `docs/10-open-questions.md`'s client-SDK/codegen row —
  removed.
- Two different tools on the GraphQL side (TypeScript vs. .NET) is a
  deliberate, stated trade — accepted because each is the strongest
  fit for its own language ecosystem, not a gap. The OpenAPI side, by
  contrast, uses **one** tool for both languages, since Kiota already
  covers both well from a single spec.
- `docs/libraries/dotnet/kiota.md`, `docs/libraries/dotnet/strawberry-
  shake.md`, and `docs/libraries/web/graphql-code-generator.md` are the
  concrete usage write-ups (this pass) — see each for install/invocation
  shape.
- Generated-code freshness is a consumer-side build concern, not
  something this framework's own CI enforces — consistent with the spec
  documents themselves being generated on-demand (`ADR-002`), never
  materialized/cached server-side.

**Verified end to end, `2026-09-04`**: Kiota (already the globally
installed `1.35.0`, no fresh install needed) generates a real C# client
from a real, populated `/openapi.json` (a real registered event type,
not a schema fixture) — mechanically, the pack → generate → compile
pipeline is sound. But calling the generated client against the real
Host failed with a real `500`: `OpenApiDocumentBuilder` (`ADR-002`)
describes `payload` as a nested JSON Schema object, while
`PublishEventRequest.Payload` is really a `string` — the generated
client's typed request body doesn't match the real wire contract.
Root cause confirmed precisely (a raw HTTP call with `payload` as a
JSON-encoded string, otherwise identical, got a real `202`). Not fixed
here — tracked as a new `TODO.md` item, since the right fix needs its
own design pass, not a guess made while proving an unrelated ADR. Full
trace in `docs/changes/2026-09-04.md`.
