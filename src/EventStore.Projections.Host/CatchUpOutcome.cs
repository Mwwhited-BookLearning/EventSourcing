namespace EventStore.Projections.Host;

// The routine, well-understood outcomes a CatchUpOnceAsync caller needs to
// distinguish for correct logging/instrumentation, collapsing
// FollowConnectResult's and ChangeKindResult's own non-happy-path cases into
// one shared shape both ProjectionHost<TReadModel> and
// EventStore.DevIdp.RbacProjectionWorker's otherwise-parallel
// TailForeverAsync loops switch on identically -- docs/patterns/known-
// outcomes-are-not-exceptions.md. `Completed` covers both "consumed one or
// more real events" and "connected but idled out with none new" -- callers
// that care about the count already get it via CatchUpOnceAsync's own
// separate `int` return.
public enum CatchUpOutcome
{
    Completed,
    UnregisteredEventType,
    Forbidden,
}
