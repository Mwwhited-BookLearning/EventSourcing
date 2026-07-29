[← Libraries index](../README.md)

# Scalar (dotnet)

**What it's for:** an interactive OpenAPI documentation UI (`Scalar.
AspNetCore`) — renders a generated OpenAPI document as a browsable,
try-it-out API reference, served directly from the same ASP.NET Core
app that generates the spec.

**Why bought, not built:** an API reference UI (endpoint list,
schema viewer, request builder) is a generic, well-solved rendering
problem with no project-specific logic in it.

## General usage

```csharp
builder.Services.AddOpenApi();
var app = builder.Build();
app.MapOpenApi();
app.MapScalarApiReference(); // serves the interactive docs UI
```

## Where this project uses it

`ADR-025` — Scalar for the OpenAPI-documented surfaces (Publish and any
remaining `GET`-only endpoints); AsyncAPI's own UI is a separate library
([`@asyncapi/react-component`](../web/asyncapi-react.md)), since OpenAPI
and AsyncAPI are different specs with different tooling. `ADR-002`
(on-demand, not materialized-cache, spec generation) is what Scalar
renders here.

## Links

- [github.com/scalar/scalar](https://github.com/scalar/scalar)
