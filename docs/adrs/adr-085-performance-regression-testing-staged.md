[← ADR index](../07-adrs.md)

# ADR-085: Performance-regression testing, staged like `ADR-063` — no framework-wide numeric targets, those are deployment-specific

Status: Accepted

Context: `README.md` states one qualitative goal ("never lose or corrupt
data") and no measurable one, yet several ADRs (`031`, `015`, `034`,
`058`) make real performance trade-offs against no stated target.
`docs/10-open-questions.md` asked both whether performance/load/soak
testing should be its own decision, distinct from `ADR-055`/`ADR-063`'s
functional/correctness coverage, and what this design's actual
throughput/latency/scale targets are. Direct design conversation
resolved this session: the two halves split cleanly — the *testing
methodology* question is framework-level and decidable now; the
*numeric targets* are partially domain-specific and shouldn't be
decided at the framework level at all.

Decision:
- **Yes, performance-regression testing is its own decision, distinct
  from `ADR-055`/`ADR-063`.** Functional/correctness tests answer "is the
  behavior right"; performance tests answer "did this change make it
  meaningfully slower than before" — a genuinely different question,
  deserving its own tooling rather than being assumed covered by
  either existing suite.
- **Staged adoption, the same shape `ADR-063` already established for
  distributed-correctness testing** — cheap now, named escalation later,
  not forced to a full production-grade harness prematurely:
  - **Adopt now: [BenchmarkDotNet](https://benchmarkdotnet.org/)**
    (micro-benchmarks with baseline comparison, purpose-built for
    catching a performance regression between two versions) against
    this design's known hot paths — the fold step, `ADR-019`'s hash-
    chain computation, `IJsonPathTranslator`'s per-provider translation.
    No new infrastructure, first-party-adjacent, already the de facto
    standard .NET benchmarking library.
  - **Deferred, named as the first move once heading toward a real
    deployment: [NBomber](https://nbomber.com/)** (a .NET-native load-
    testing framework, protocol-agnostic) for actual end-to-end load/
    soak testing against a running deployment — not adopted now because
    there's no real deployment target to load-test against yet, the
    same reasoning `ADR-063` already applied to deferring `Toxiproxy`.
- **No framework-wide numeric targets (events/sec, fold lag, query
  latency, tenant/entity/event ceilings) are set — this is the actual
  resolution, not an oversight.** The question's own premise assumed a
  single number could describe every deployment, but this design is
  explicitly multi-tenant and domain-agnostic (`ADR-030`/`ADR-075`) — a
  clinical-trials site's telemetry volume and a hypothetical high-
  volume IoT deployment's have nothing in common, and a fixed framework
  target would either be meaninglessly loose for one or impossible for
  the other. Numeric targets are **deployment-time capacity planning**,
  the same posture `ADR-058` already takes for per-tenant rate limits
  (a configuration value, not a hardcoded framework constant) — not
  decided here, and not meant to be.
- **What the framework *does* owe every deployment: the benchmark
  suite's own baseline stays meaningful regardless of target.**
  BenchmarkDotNet's regression check ("did this change make the hot
  path slower than its own prior baseline") needs no external target at
  all — it's a relative, not absolute, measurement, so it's exactly the
  half of this question a domain-agnostic framework *can* own without
  presuming any particular deployment's scale.

Consequences:
- ~~`docs/libraries/dotnet/` gains entries for BenchmarkDotNet (adopted
  now) and NBomber (named future escalation) — propagation work, not
  done in this pass.~~ **Done — found stale by a design-compliance
  audit this session**: `docs/libraries/dotnet/benchmarkdotnet.md`
  exists; `src/EventStore.Benchmarks/` (referencing BenchmarkDotNet
  0.15.8) is real. NBomber correctly remains un-adopted, as decided
  above — no premature adoption, not an oversight.
- A deployment wanting real throughput/latency SLAs still needs its own
  capacity-planning exercise once real usage data exists — explicitly
  out of this ADR's scope, not silently assumed solved.
- Resolves the design fork logged in `docs/changes/2026-07-31.md`
  (formerly `docs/10-open-questions.md` row 10).
