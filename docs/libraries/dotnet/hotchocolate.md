[← Libraries index](../README.md)

# HotChocolate (dotnet)

**What it's for:** a GraphQL server for .NET (ChilliCream) — turns C#
types/resolvers into a spec-compliant schema and handles parsing,
validation, and execution, with first-class Subscriptions (WebSocket),
DataLoader-based batching, and OpenTelemetry integration.

**Why bought, not built:** `ADR-037` committed this design to GraphQL as
the sole query layer, but never named a concrete server library — a real
gap this pass closed, not a request by name. Implementing a spec-
compliant GraphQL execution engine (parsing, validation, the partial-
success `data`/`errors` execution model, Subscriptions, N+1-safe
batching) from scratch would be enormous, and every piece of it is
already `ADR-037`'s own stated requirement, not incidental — the
textbook case for buying instead of building.

## General usage

```csharp
builder.Services
    .AddGraphQLServer()
    .AddQueryType<EntityQueries>()
    .AddSubscriptionType<EventSubscriptions>()
    .AddDataLoader<EntityByIdDataLoader>();

public class EntityQueries
{
    public async Task<Entity> GetEntity(string entityId, [Service] IEntityStore store)
        => await store.GetAsync(entityId);
}
```

```csharp
app.MapGraphQL(); // exposed over HTTP QUERY per ADR-012, not POST — see below
```

HotChocolate defaults to `POST`; exposing it over the HTTP `QUERY`
method instead (`ADR-012`'s retargeting, for the PII-in-URL reason
`ADR-037` states) needs a small custom endpoint mapping rather than
`MapGraphQL()`'s default — a real integration detail for whoever builds
this, not something HotChocolate does out of the box.

## Where this project uses it

`ADR-037` — the concrete server behind the GraphQL Gateway (`01-c4-
architecture.md`), including depth/cost limiting (built-in
`MaxAllowedExecutionDepth`/complexity analysis) and DataLoader batching
across `ADR-034`'s shards/`ADR-033`'s replicas, both already mandated
by that ADR.

## Links

- [chillicream.com/docs/hotchocolate](https://chillicream.com/docs/hotchocolate)
- [github.com/ChilliCream/graphql-platform](https://github.com/ChilliCream/graphql-platform)
