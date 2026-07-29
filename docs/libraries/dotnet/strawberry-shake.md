[← Libraries index](../README.md)

# Strawberry Shake (dotnet)

**What it's for:** ChilliCream's GraphQL client for .NET
([chillicream.com/docs/strawberryshake](https://chillicream.com/docs/strawberryshake/)) —
generates strongly-typed C# classes and a reactive, cached client store
from `.graphql` operation documents checked against a schema, in the same
spirit as Apollo Client/Relay on the JS side.

**Why bought, not built:** same reasoning as `HotChocolate` itself
(`docs/libraries/dotnet/hotchocolate.md`) — a spec-compliant GraphQL
client with typed operations, reactive caching, and subscription support
is a large, general problem with no project-specific value in
reimplementing it. Picked specifically over a generic/unaffiliated .NET
GraphQL client because it's built by **ChilliCream, the same vendor as
the server-side `HotChocolate`** (`ADR-037`) — schema conventions and any
ChilliCream-specific behavior stay in one vendor's hands across both
ends of the wire, rather than a client library independently guessing at
server behavior.

## General usage

```bash
dotnet graphql init https://localhost:5001/graphql --clientName EventStoreClient
```

```graphql
# EventHistory.graphql
query EntityHistory($entityId: ID!) {
  entityHistory(entityId: $entityId) {
    occurredAt
    changeKind
  }
}
```

```csharp
var result = await client.EntityHistory.ExecuteAsync(entityId);
var history = result.Data.EntityHistory;
```

## Where this project uses it

`ADR-054` — generates the GraphQL-side (query/subscribe) client for .NET
consumers, from `ADR-037`'s SDL. Regenerated at the consuming
application's own build time, the same posture `ADR-054` establishes for
`Kiota`'s OpenAPI-side client — a schema change is discovered at the
consumer's next build, not silently drifted past.

## Links

- [chillicream.com/docs/strawberryshake](https://chillicream.com/docs/strawberryshake/)
- [github.com/ChilliCream/graphql-platform](https://github.com/ChilliCream/graphql-platform)
