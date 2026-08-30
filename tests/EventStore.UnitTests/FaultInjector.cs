namespace EventStore.UnitTests;

// Replaces Polly.Contrib.Simmy (ADR-063's original in-process fault-injection
// mechanism) -- removed given Polly's new Open Source Maintenance Fee
// (thepollyproject.org, 2026-07-14, a per-organization usage fee that would
// apply here too once this reference framework generates real revenue for an
// adopter), direct request. What this project ever actually used Simmy for --
// injecting a fake exception before a real call, at a configurable rate --
// is small enough to not need a third-party dependency at all: both existing
// call sites (PublishCrashRecoveryFaultInjectionTests) always used
// InjectionRate(1.0) (unconditional), so this keeps the same [0,1]-rate
// convention Simmy used, in case a future test wants a genuinely partial
// rate, rather than hard-coding away that capability.
internal static class FaultInjector
{
    public static async Task<T> InjectAsync<T>(Func<Task<T>> action, Exception fault, double rate = 1.0, Random? random = null)
    {
        if ((random ?? Random.Shared).NextDouble() < rate)
            throw fault;
        return await action();
    }
}
