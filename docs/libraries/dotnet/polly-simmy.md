[← Libraries index](../README.md)

# Polly + Simmy (dotnet)

**Removed** (`docs/bugs/framework/service/follow-client-faults-under-
default-http-resilience-timeout.md`, `ADR-063`'s own additive
implementation note) — replaced with a minimal, hand-rolled
`FaultInjector` (`tests/EventStore.UnitTests/FaultInjector.cs`), direct
request, driven by Polly's new Open Source Maintenance Fee
([thepollyproject.org, 2026-07-14](https://thepollyproject.org/2026/07/14/polly-osmf-announcement.html))
— a per-organization usage fee, triggered by revenue rather than by
which specific Polly package is used, that would apply to this reference
framework's own test suite too if an adopter ever commercialized it.
Both real call sites in this solution always used `InjectionRate(1.0)`
(unconditional fault injection), never the partial, configurable rate
that was this library's own original "worth buying" reasoning below —
kept as historical record of that reasoning, not because it's still
accurate advice for this project.

**What it was for:** `Polly` is .NET's de facto resilience library (retry,
circuit breaker, timeout, fallback policies); `Simmy` is its
chaos-engineering companion — it wraps a real call and, at a configured
rate, injects a fake failure (an exception, a fake timeout, a slow
response) so a test can prove the surrounding code actually handles that
failure, without touching the real dependency. **Verified package identity
(checked against the installed `.nuspec`, not assumed):** this project
depends on the separate `Polly.Contrib.Simmy` NuGet package (v0.3.0,
BSD-3-Clause, `App vNext`), which itself depends on `Polly` v7.1.0 — it is
its own companion package, not functionality folded into `Polly` v8's
resilience pipelines. Its real namespaces are `Polly.Contrib.Simmy` and
`Polly.Contrib.Simmy.Outcomes` (not the bare `Simmy`/`Simmy.Fault` an
earlier draft of this integration guessed before checking).

**Why bought, not built:** correctly injecting faults at a configurable
rate, without corrupting the real call path when disabled, is exactly
the kind of small-but-easy-to-get-subtly-wrong infrastructure worth
buying. `Polly` is also the obvious first stop for genuine resilience
policies (retry/circuit-breaker) if this design ever needs them for
real, not just to test them.

## General usage (historical — this project's own real usage now below)

```csharp
var chaosPolicy = MonkeyPolicy.InjectExceptionAsync(with => with
    .Fault(new IOException("simulated abend"))
    .InjectionRate(1.0)
    .Enabled(true));

// Wrap a peer-sync delivery call; assert the durable PeerSyncCursor
// (ADR-033) still resumes from the last real checkpoint after the
// injected failure, never re-delivering or losing an event.
await Assert.ThrowsExactlyAsync<IOException>(
    () => chaosPolicy.ExecuteAsync(() => peerSync.DeliverAsync(batch)));
```

## What replaced it in this project

```csharp
// tests/EventStore.UnitTests/FaultInjector.cs
await Assert.ThrowsExactlyAsync<IOException>(() => FaultInjector.InjectAsync(
    () => peerSync.DeliverAsync(batch), new IOException("simulated abend")));
```

Same `[0,1]` injection-rate convention (defaults to `1.0`, matching every
real call site this project ever had), no third-party dependency. See
`tests/EventStore.UnitTests/PublishCrashRecoveryFaultInjectionTests.cs`
for the real, exercised shape.

## Where this project used it

`ADR-063` — in-process fault injection proving `ADR-033`/`ADR-039`'s
durable outbox/inbox actually resumes correctly after a simulated crash,
inside `ADR-055`'s `EventStore.UnitTests`. Now via `FaultInjector`
instead, same tests, same coverage.

## Links

- [github.com/App-vNext/Polly](https://github.com/App-vNext/Polly)
- [Polly chaos engineering docs](https://github.com/App-vNext/Polly/blob/main/docs/chaos/index.md)
