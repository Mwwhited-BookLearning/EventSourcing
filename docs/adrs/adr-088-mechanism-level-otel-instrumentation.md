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
    Entity Store (`ADR-021`).
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
