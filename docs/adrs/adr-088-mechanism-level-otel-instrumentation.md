[← ADR index](../07-adrs.md)

# ADR-088: Mechanism-level OpenTelemetry instrumentation — detailed metrics/logging/tracing for the framework's own async mechanisms, and for domains

Status: Accepted — extends `ADR-026`

Context: `docs/10-open-questions.md` row 7 asked whether this design
should define SLIs/SLOs (router fold lag, peer-sync outbox depth/age,
webhook delivery lag, hash-chain verification success) with alert
thresholds, and whether an incident-response/on-call process is in
framework scope — back-burnered earlier this session as an operations/
runbook concern. Direct design conversation narrowed it: the *SLI* half
(what gets measured/emitted) is a real dev decision, not an ops one —
`ADR-026` already wires generic ASP.NET Core/HttpClient/runtime
instrumentation, but nothing emits a custom signal for any of this
design's own mechanisms. **The alert-threshold/SLO/on-call half stays
exactly as back-burnered as before** — this ADR resolves only what gets
measured, not what threshold matters or who responds.

Decision:
- **The framework emits custom metrics for its own async mechanisms**,
  via `System.Diagnostics.Metrics` (`Meter`/`Counter<T>`/`Histogram<T>`/
  `ObservableGauge<T>` — first-party, part of the runtime since .NET 6,
  already OpenTelemetry-compatible), registered into `ADR-026`'s
  existing pipeline with one additional `.AddMeter("Duplex.Core")` call
  — extending what's already wired, not a new observability stack:
  - **Router fold lag** — a `Histogram<double>` measuring elapsed time
    between an event's `SequenceNumber` assignment and its fold into the
    Entity Store (`ADR-021`) — **recorded only for events that fold
    immediately** (`AuthorityStatus` already `accepted` at publish,
    `ADR-042`'s default for an ordinary authenticated publish). An event
    gated through `unattested`/`pending_review` waits on open-ended human
    review, not processing time — mixing that into the same histogram
    would conflate a mechanism latency with a review-workflow duration
    that has nothing to do with fold performance. `AuthorityDecisionRef`
    turnaround (self-attested/detector-triggered publishes specifically)
    is a distinct, separate metric if a deployment wants it, not folded
    into this one.
  - **Peer-sync outbox depth/age** — an `ObservableGauge<long>` per peer,
    reporting the outbox's current pending-item count and the age of its
    oldest pending item (`ADR-033`).
  - **Webhook delivery lag** — a `Histogram<double>` measuring elapsed
    time between an event queued for delivery and confirmed delivery
    (`ADR-060`).
  - **Hash-chain verification outcomes** — a `Counter<long>` incremented
    per verification attempt, tagged by outcome (`ADR-019`).
- **The framework emits custom traces for the same mechanisms — already
  structurally free.** `ADR-026`'s `AddSource(builder.Environment.
  ApplicationName)` already collects any `ActivitySource` named after
  the application; the fold step, each outbox pump, and the hash-chain
  verifier each wrap their work in a named `Activity` — no pipeline
  change needed, just using what's already collected.
- **Domains follow the identical convention for their own domain-
  specific operations** (a detector's processing latency, a domain
  business metric) — the same `Meter`/`ActivitySource` pattern, not a
  second observability mechanism invented per domain. This is
  architectural guidance the framework provides, the same shape
  `ADR-087` just established for i18n: the framework owns the
  convention, a domain's own application code owns what it specifically
  measures.
- **Still explicitly deferred, unchanged from the earlier back-burner
  note**: alert thresholds (what numeric value triggers a page) and the
  incident-response/on-call process itself remain genuine deployment-
  specific operational policy — this ADR supplies the signal, not the
  response to it.

Consequences:
- `EventStore.ServiceDefaults`'s `ConfigureOpenTelemetry` gains the
  `.AddMeter` call and the mechanism code gains the actual `Meter`/
  `ActivitySource` instances — not yet built, propagation work.
- Row 7 is narrowed, not fully resolved — the SLI/instrumentation half
  is decided; the SLO/alerting/on-call half stays open and deprioritized
  exactly as before.

**Corrected, 2026-08-11 (build-plan item 47's own implementation pass)**:
the trace half's "already structurally free... no pipeline change
needed" claim above did not survive contact with implementation. It
assumed each mechanism's own `ActivitySource` would be named to match
`builder.Environment.ApplicationName` (`ADR-026`'s existing
`AddSource(...)` call), resolved via a DI-injected `IHostEnvironment`.
That doesn't hold for the shared, static-instance shape actually built:
`RouterWorker.RunOnceAsync` and its peer static entry points across
`EventStore.Replication`/`Webhooks` are directly callable by a test
with no DI container in scope at all — the same seam their existing
optional `erasureKeyService`/`payloadMasker` parameters already depend
on. (`EventStore.Inbox`'s own `ChainVerificationService` is an ordinary
DI-injected, instance-based service, not a static entry point — it
reaches the same shared static instance below for consistency with the
other three mechanisms, not because it lacks a container of its own.)
The only way a single shared `Meter`/`ActivitySource` instance (named
`"Duplex.Core"`, living in `EventStore.Domain/Observability/
DuplexInstrumentation.cs` — the one project already a common dependency
of all four mechanism projects) can be reached from Router/Replication/
Webhooks' own static methods is a static field, so it was made the ONE
shared instance every mechanism uses uniformly. A second, explicit
`.AddSource("Duplex.Core")` call was added instead,
mirroring the metrics `.AddMeter("Duplex.Core")` call exactly — one
added pipeline line, not zero, but still far short of "a new
observability stack." The Decision text above is left as originally
written (additive-history convention) rather than silently edited; this
note is the correction.

**Extended, 2026-08-13 (direct request — "setup the otel metrics as
well," then "add new metrics," then "all standard .net and asp.net
metrics as well as any application details that might be useful")**: the
Decision text above named four mechanisms (Router, peer-sync outbox,
webhooks, hash-chain verification) — real gaps existed beyond them,
closed this pass on the same shared `Duplex.Core` `Meter`, no new
pipeline wiring beyond what's already described above:
- **Publish throughput/latency** — `PublishOutcomes`
  (`Counter<long>`, tagged `app.id`/`event.type`/`outcome`) and
  `PublishLatencyMs` (`Histogram<double>`), recorded by a thin
  `PublishService.PublishAsync` wrapper around the renamed
  `PublishAsyncCore` — one Stopwatch covers every one of that method's
  several early-return outcomes uniformly, rather than instrumenting
  each branch.
- **GraphQL request volume/latency** — the one query-layer gap this
  ADR's original Decision text never covered at all (Router/Webhooks/
  Replication/Inbox only). `HotChocolate.Diagnostics`' own OpenTelemetry
  integration (`AddInstrumentation`/`AddHotChocolateInstrumentation`,
  checked directly against its v16 docs page before deciding not to add
  it) produces spans, never a `Meter` — so `GraphQlRequestOutcomes`/
  `GraphQlRequestLatencyMs` are recorded by a new
  `GraphQlDiagnosticEventListener` (`EventStore.GraphQL`) extending
  HotChocolate's own abstract `ExecutionDiagnosticEventListener`
  (`HotChocolate.Execution.Instrumentation`), registered via
  `.AddDiagnosticEventListener<T>()`. Found only by letting the compiler
  enumerate the real member set after reflection against the installed
  assembly proved insufficient (it under-reported: `StartProcessing`/
  `StopProcessing` are real, per that reflection check, but a much larger
  `ICoreExecutionDiagnosticEvents` base surface lived in an assembly that
  same reflection pass never actually loaded) — the same "verify before
  citing" discipline `FollowSubscriptionTypeModule`'s own comment already
  establishes for this library, taken one step further than reflection
  alone this time.
- **Derivation lag** — `DerivationLagMs` (`Histogram<double>`, tagged
  `app.id`/`derivation.name`), `DerivationWorker`'s own analogue of
  Router fold lag above: elapsed time between the triggering source
  event's `AppendedAt` and the derived event's successful publish.
- **Archival operation count/duration** — `ArchivalSegmentsArchived`
  (`Counter<long>`, tagged `log`/`outcome`) and
  `ArchivalOperationDurationMs` (`Histogram<double>`, tagged `log`).
  `ArchivalService` has no polling worker of its own (this ADR's sibling
  gap, `ADR-056`, still not built) — no "lag" to measure the way the
  three worker-shaped mechanisms above have, so this measures the
  on-demand operation itself instead.
- **Simulator publish rate** — `SimulatorEventsPublished`
  (`Counter<long>`, tagged `app.id`), recorded by
  `Samples.Vitals.Simulator`/`Samples.Meridian.Simulator` directly. Both
  had **zero** OTel wiring before this pass (a plain
  `Host.CreateApplicationBuilder`, no `ConfigureOpenTelemetry` call) —
  Aspire injects `OTEL_EXPORTER_OTLP_ENDPOINT` into every project
  resource regardless, but nothing was listening for it. Both now call
  `EventStore.ServiceDefaults`' `ConfigureOpenTelemetry()` directly
  (not the full `AddServiceDefaults()` — a plain console loop with no
  HTTP surface has no use for the health-check/service-discovery/HTTP-
  resilience wiring that bundles), a new `ProjectReference` each.
- **Standard .NET process metrics** — `OpenTelemetry.Instrumentation.
  Process`'s `AddProcessInstrumentation()` (CPU%, working set, thread/
  handle count), added to `EventStore.ServiceDefaults` alongside the
  pre-existing AspNetCore/Http/Runtime instrumentation. The one
  inconsistency worth flagging: this package has never shipped a stable
  release (`1.17.0-rc.1` as of this pass, checked directly against
  nuget.org's own version listing before adding it) — added anyway,
  since there's no stable alternative for process-level metrics and the
  surface has been release-candidate for a long time.

All additions verified by actually building every touched project (and
the full solution) after each change, plus a full `EventStore.
IntegrationTests` run (224/224) after the transaction-strategy fix this
same pass required first (see `docs/changes/2026-08-13.md`) — not just
assumed to compile.
