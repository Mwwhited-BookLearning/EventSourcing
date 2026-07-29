[← Libraries index](../README.md)

# .NET Aspire (dotnet)

**What it's for:** an opinionated, code-first orchestration layer for
local development of a multi-project distributed app — service
discovery, container/process lifecycle, and a live dashboard for logs/
traces/metrics — plus `ServiceDefaults`, a shared set of extension
methods that wires OpenTelemetry, health checks, and resilience into
every project consistently.

**Why bought, not built:** hand-rolling "start these N projects plus a
database container, wire up service discovery between them, and show me
combined logs/traces" is a real amount of infrastructure code with no
project-specific value in it — exactly the profile of a solved problem
to buy rather than build.

## General usage

```csharp
// EventStore.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);
var db = builder.AddPostgres("eventstore-db");
var idp = builder.AddProject<Projects.EventStore_DevIdp>("devidp");
builder.AddProject<Projects.EventStore_Host_Postgres>("api")
    .WithReference(db)
    .WithReference(idp);
builder.Build().Run();
```

```csharp
// Every project's Program.cs
builder.AddServiceDefaults(); // OpenTelemetry logging/tracing/metrics, health checks
```

## Where this project uses it

`ADR-006` (orchestrating `EventStore.DevIdp` alongside the API host),
`ADR-026` (the full dev-time OpenTelemetry story — logging, tracing,
metrics — via `ServiceDefaults`; production instead uses Docker Compose,
per the same ADR).

## Links

- [learn.microsoft.com/dotnet/aspire](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview)
