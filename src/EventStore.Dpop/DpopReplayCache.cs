using System.Collections.Concurrent;

namespace EventStore.Dpop;

// RFC 9449 requires jti-tracked replay detection -- a proof is single-use.
// In-memory, per-process, no eviction beyond a lazy sweep on write: this is
// a small, fixed set of trusted dev/POC clients (ADR-017's own scoping),
// not a durability requirement -- a process restart losing replay history
// is an accepted v1 cost, the same posture ADR-017 takes on skipping the
// RFC 9449 §8 nonce challenge entirely for v1.
public interface IDpopReplayCache
{
    // Returns true (and registers jti) the first time it's seen before
    // expiresAt; false if jti was already registered -- a replay.
    bool TryRegister(string jti, DateTimeOffset expiresAt);
}

public sealed class InMemoryDpopReplayCache : IDpopReplayCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new();

    public bool TryRegister(string jti, DateTimeOffset expiresAt)
    {
        SweepExpired();
        return _seen.TryAdd(jti, expiresAt);
    }

    private void SweepExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (jti, expiresAt) in _seen)
            if (expiresAt < now)
                _seen.TryRemove(jti, out _);
    }
}
