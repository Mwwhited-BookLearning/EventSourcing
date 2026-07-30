[← Libraries index](../README.md)

# Polly + Simmy (dotnet)

**What it's for:** `Polly` is .NET's de facto resilience library (retry,
circuit breaker, timeout, fallback policies); `Simmy` is its
chaos-engineering companion (merged directly into `Polly` v8) — it wraps
a real call and, at a configured rate, injects a fake failure (an
exception, a fake timeout, a slow response) so a test can prove the
surrounding code actually handles that failure, without touching the
real dependency.

**Why bought, not built:** correctly injecting faults at a configurable
rate, without corrupting the real call path when disabled, is exactly
the kind of small-but-easy-to-get-subtly-wrong infrastructure worth
buying. `Polly` is also the obvious first stop for genuine resilience
policies (retry/circuit-breaker) if this design ever needs them for
real, not just to test them.

## General usage

```csharp
var chaosPolicy = MonkeyPolicy.InjectException(with =>
    with.Fault(new IOException("simulated abend"))
        .InjectionRate(0.3)
        .Enabled());

// Wrap a peer-sync delivery call; assert the durable PeerSyncCursor
// (ADR-033) still resumes from the last real checkpoint after the
// injected failure, never re-delivering or losing an event.
await chaosPolicy.ExecuteAsync(() => peerSync.DeliverAsync(batch));
```

## Where this project uses it

`ADR-063` — in-process fault injection proving `ADR-033`/`ADR-039`'s
durable outbox/inbox actually resumes correctly after a simulated crash,
inside a new suite alongside `ADR-055`'s `EventStore.UnitTests`.

## Links

- [github.com/App-vNext/Polly](https://github.com/App-vNext/Polly)
- [Polly chaos engineering docs](https://github.com/App-vNext/Polly/blob/main/docs/chaos/index.md)
