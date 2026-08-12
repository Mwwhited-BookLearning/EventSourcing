[← Libraries index](../README.md)

# Polly + Simmy (dotnet)

**What it's for:** `Polly` is .NET's de facto resilience library (retry,
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

## General usage

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

(The exact shape verified in this repo's own tests — see
`tests/EventStore.UnitTests/PublishCrashRecoveryFaultInjectionTests.cs`.)

## Where this project uses it

`ADR-063` — in-process fault injection proving `ADR-033`/`ADR-039`'s
durable outbox/inbox actually resumes correctly after a simulated crash,
inside a new suite alongside `ADR-055`'s `EventStore.UnitTests`.

## Links

- [github.com/App-vNext/Polly](https://github.com/App-vNext/Polly)
- [Polly chaos engineering docs](https://github.com/App-vNext/Polly/blob/main/docs/chaos/index.md)
