[← Libraries index](../README.md)

# GraphQL Code Generator (web)

**What it's for:** the de facto standard TypeScript codegen tool for
GraphQL ([the-guild.dev/graphql/codegen](https://the-guild.dev/graphql/codegen))
— reads a GraphQL schema plus a project's own `.graphql` operation
documents and generates fully-typed TypeScript operations, and typed
hooks for common frameworks (React, Vue, Angular) via plugins.

**Why bought, not built:** same reasoning as every other codegen tool
this ADR adopts (`ADR-054`) — the schema is already machine-readable
(`ADR-037`'s SDL), so a typed client is a mechanical projection of it,
not new design. Picked specifically because it's the standard tool for
*this* language's GraphQL ecosystem — TypeScript — distinct from
`Strawberry Shake` on the .NET side (`docs/libraries/dotnet/
strawberry-shake.md`); the GraphQL client tooling ecosystem splits
cleanly by target language, unlike the OpenAPI side where one tool
(`Kiota`) already covers both languages this project needs.

## General usage

```yaml
# codegen.yml
schema: https://localhost:5001/graphql
documents: 'src/**/*.graphql'
generates:
  src/generated/graphql.ts:
    plugins:
      - typescript
      - typescript-operations
      - typescript-vue-apollo
```

```typescript
import { useEntityHistoryQuery } from '@/generated/graphql'

const { result } = useEntityHistoryQuery({ entityId: props.entityId })
```

## Where this project uses it

`ADR-054` — generates the GraphQL-side (query/subscribe) TypeScript
client, from `ADR-037`'s SDL. Regenerated at the consuming application's
own build time — the same posture `ADR-054` establishes for every
generated client this framework's consumers use, `ADR-039`'s Vue/Pinia
reference app included, if it's ever updated to consume a generated
client rather than hand-written fetch calls.

## Links

- [the-guild.dev/graphql/codegen](https://the-guild.dev/graphql/codegen)
- [github.com/dotansimha/graphql-code-generator](https://github.com/dotansimha/graphql-code-generator)
