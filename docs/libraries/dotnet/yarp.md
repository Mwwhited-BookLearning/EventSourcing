[← Libraries index](../README.md)

# YARP (dotnet)

**What it's for:** "Yet Another Reverse Proxy" — a toolkit for building a
reverse proxy in ASP.NET Core: config-driven or code-driven routing,
load balancing, and request/response transforms, without hand-rolling
HTTP forwarding.

**Why bought, not built:** a reverse proxy has to get header
forwarding, connection pooling, and streaming request/response bodies
right — a lot of fiddly HTTP-protocol correctness with no
project-specific value in reimplementing it, and it's Microsoft's own
first-party library (`ADR-041`'s preference).

## General usage

```csharp
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

app.MapReverseProxy();
```

```json
{
  "ReverseProxy": {
    "Routes": {
      "graphql": { "ClusterId": "graphql-cluster", "Match": { "Path": "/graphql/{**catch-all}" } },
      "attachments": { "ClusterId": "attachments-cluster", "Match": { "Path": "/attachments/{**catch-all}" } }
    },
    "Clusters": {
      "graphql-cluster": { "Destinations": { "d1": { "Address": "https://eventstore-graphql/" } } },
      "attachments-cluster": { "Destinations": { "d1": { "Address": "https://eventstore-attachments/" } } }
    }
  }
}
```

## Where this project uses it

`ADR-049` — the single external entry point in front of this design's
now-multiple independently-addressable services (GraphQL Gateway,
attachment retrieval, streaming playback, ticket/OAuth endpoints).
External TLS termination and `ADR-006`/`ADR-017`/`ADR-040`
authentication happen here; internal gateway-to-service calls use
[`ADR-048`'s SPIFFE/SPIRE](../../adrs/adr-048-spiffe-spire-service-identity.md)
workload identity instead.

## Links

- [github.com/dotnet/yarp](https://github.com/dotnet/yarp)
