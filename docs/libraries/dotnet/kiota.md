[← Libraries index](../README.md)

# Kiota (dotnet — generates both C# and TypeScript)

**What it's for:** Microsoft's first-party OpenAPI-based HTTP client
generator ([microsoft/kiota](https://github.com/microsoft/kiota)) — reads
an OpenAPI description and produces a strongly-typed request-builder
client, with C#, TypeScript, Java, Go, Python, PHP, Ruby, and Swift all
as real generation targets from the same spec.

**Why bought, not built:** hand-writing (or hand-maintaining a
hand-written) HTTP client against a schema that already exists and is
already machine-readable (`ADR-002`'s generated `openapi.json`) is the
textbook case for codegen over hand authorship — every property/endpoint
Kiota generates is already declared in the spec, so nothing is invented,
only mechanically projected into a typed client. Picked over `NSwag`/
`OpenAPI Generator` specifically because it's Microsoft's own tool
(`ADR-041`'s first-party preference) and covers **both** languages this
project needs (C# and TypeScript) from one tool, rather than reaching
for two separate community generators with two different conventions.

## General usage

```bash
# Generate a C# client
kiota generate -l CSharp -d https://localhost:5001/openapi.json -o ./generated/csharp -c EventStoreClient -n EventStore.Client

# Generate a TypeScript client from the same spec
kiota generate -l TypeScript -d https://localhost:5001/openapi.json -o ./generated/typescript -c eventStoreClient
```

```csharp
var client = new EventStoreClient(requestAdapter);
await client.Publish[eventType].PostAsync(payload);
```

```typescript
const client = createEventStoreClient(requestAdapter);
await client.publish.byEventType(eventType).post(payload);
```

## Where this project uses it

`ADR-054` — generates the publish-side (OpenAPI) client for both .NET
and TypeScript consumers, from `ADR-002`'s always-current, on-demand
`openapi.json`. Run at the consuming application's own build time (CLI
or the Kiota VS Code extension), not committed as generated code living
in this repository — the framework publishes the spec; each consumer
regenerates against the version they target.

## Links

- [github.com/microsoft/kiota](https://github.com/microsoft/kiota)
- [learn.microsoft.com/openapi/kiota](https://learn.microsoft.com/en-us/openapi/kiota/)
