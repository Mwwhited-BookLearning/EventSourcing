[← ADR index](../07-adrs.md)

# ADR-026: Development via .NET Aspire + OpenTelemetry (logging, tracing, metrics); production via Docker Compose

See [docs/libraries/dotnet/aspire.md](../libraries/dotnet/aspire.md) for
Aspire usage examples.

Status: Accepted — refines `ADR-006`'s orchestration story; does not
change its auth/client decisions.

Context: `ADR-006` already introduced both `EventStore.AppHost` (.NET
Aspire) and a root `docker-compose.yml`, but framed the split loosely —
Aspire "preferred day-to-day," compose as "a non-Aspire-tooling
fallback... e.g. CI." `06-solution-structure.md` mentions
`EventStore.ServiceDefaults` wiring "OpenTelemetry, health checks, service
discovery" in one line, with no detail on which OpenTelemetry signals
are actually configured. Both need to be made concrete and explicit
rather than left as a vague aspiration.

Decision:
- **Local development: .NET Aspire, with all three OpenTelemetry signals
  configured, not just mentioned.** `EventStore.ServiceDefaults`'
  `ConfigureOpenTelemetry` (called from every `EventStore.Host.<Provider>`,
  `EventStore.DevIdp`, and `EventStore.Projections.Host` — `ADR-001`,
  `ADR-006`, `ADR-015`) wires:
  ```csharp
  public static void ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
  {
      builder.Logging.AddOpenTelemetry(o => { o.IncludeFormattedMessage = true; o.IncludeScopes = true; });

      builder.Services.AddOpenTelemetry()
          .WithMetrics(m => m
              .AddAspNetCoreInstrumentation()
              .AddHttpClientInstrumentation()
              .AddRuntimeInstrumentation())
          .WithTracing(t => t
              .AddSource(builder.Environment.ApplicationName)
              .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
                  !ctx.Request.Path.StartsWithSegments(HealthEndpointPath) &&
                  !ctx.Request.Path.StartsWithSegments(AlivenessEndpointPath))
              .AddHttpClientInstrumentation());

      var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
      if (!string.IsNullOrWhiteSpace(otlpEndpoint))
          builder.Services.AddOpenTelemetry().UseOtlpExporter();
  }
  ```
  All three pillars — logs, traces, metrics — every time, for every
  service in the solution; not an opt-in per project. `EventStore.AppHost`
  injects `OTEL_EXPORTER_OTLP_ENDPOINT` automatically for every resource it
  orchestrates, pointed at the Aspire Dashboard's OTLP receiver — this is
  Aspire's standard local-dev telemetry sink, not a separate piece to
  stand up.
- **Production: Docker Compose, not Aspire.** `ADR-006`'s
  `docker-compose.yml` stops being framed as a fallback and becomes *the*
  production deployment path for this design — Aspire's AppHost is a
  local-orchestration/dev-loop tool (it targets `dotnet run`/`aspire run`
  workflows, not a production container orchestrator), while
  `docker-compose.yml` builds the same ordinary app images
  (`EventStore.Host.<Provider>`, `EventStore.DevIdp` — or its production
  IdP replacement — and now `EventStore.Projections.Host`) and runs them
  without any Aspire-specific machinery. Both paths deploy the identical
  container images; only what launches/wires them differs by environment.
- `OTEL_EXPORTER_OTLP_ENDPOINT` in production points at whatever
  OTLP-compatible collector the deployment target has (unspecified here —
  an environment-level choice, not fixed by this design) rather than the
  Aspire Dashboard, which is dev-only. `ConfigureOpenTelemetry`'s
  conditional-exporter check (`AddOpenTelemetryExporters`, above) is what
  makes this the same code path in both environments — telemetry wiring
  doesn't fork between dev and prod, only where it's sent does.

Consequences:
- Every service gets consistent logging/tracing/metrics with zero
  per-project configuration — a real, immediate payoff of centralizing
  this in `EventStore.ServiceDefaults` rather than each host wiring its
  own subset.
- Health-check/liveness requests are deliberately excluded from traces
  (the `Filter` above) — keeps the trace view meaningful under Aspire's
  own frequent health-polling, not cluttered with noise from the
  orchestrator's own checks.
- `docker-compose.yml` being the production path (not just a CI fallback)
  means it needs to be kept genuinely deployment-ready — env-based
  secrets/connection strings, not dev-convenience defaults — a real
  standard to hold it to that "CI fallback" framing didn't previously
  demand.
- No change to `ADR-006`'s actual auth/client model, or to `ADR-001`'s
  per-provider build — this ADR is purely about *how a deployment is
  launched and observed*, the same boundary `ADR-006`'s own consequences
  already drew around what Aspire does and doesn't affect.
