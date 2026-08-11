namespace EventStore.Domain.EventLog;

// ADR-033 -- a Hybrid Logical Clock, not a plain vector clock: bounded
// size regardless of how many origins have ever been seen, unlike a true
// vector clock that grows with the origin count. Standard HLC algorithm
// (Kulkarni et al.): the physical component never regresses (it's the max
// of wall-clock time and every physical component seen so far, local or
// remote); the logical component only increments when two events would
// otherwise tie on the physical component, and resets to 0 the moment
// physical time genuinely advances past both. String-formatted as
// "{physicalTicks:D19}-{logicalCounter:D10}" so two clock VALUES compare
// correctly via ordinary string/lexicographic comparison too, not just via
// this type's own Parse.
public static class HybridLogicalClock
{
    // previousLocal: the most recent LogicalClock this same site has
    // already issued (read from the prior row in the same append
    // transaction, the identical "read prior state, compute next" shape
    // EventChainHash already uses). observedRemote: for a peer-sync-
    // received event, that event's own LogicalClock as stamped at its
    // origin site -- merged in so this site's clock never falls behind
    // a clock value it has now observed.
    public static string Next(string? previousLocal, string? observedRemote = null, DateTimeOffset? now = null)
    {
        var (prevPhysical, prevLogical) = Parse(previousLocal);
        var (remotePhysical, remoteLogical) = Parse(observedRemote);
        var wallPhysical = (now ?? DateTimeOffset.UtcNow).UtcTicks;

        var physical = Math.Max(wallPhysical, Math.Max(prevPhysical, remotePhysical));
        long logical;
        if (physical == prevPhysical && physical == remotePhysical)
            logical = Math.Max(prevLogical, remoteLogical) + 1;
        else if (physical == prevPhysical)
            logical = prevLogical + 1;
        else if (physical == remotePhysical)
            logical = remoteLogical + 1;
        else
            logical = 0;

        return Format(physical, logical);
    }

    private static (long Physical, long Logical) Parse(string? clock)
    {
        if (string.IsNullOrEmpty(clock))
            return (0, 0);
        var separatorIndex = clock.IndexOf('-');
        return (long.Parse(clock[..separatorIndex]), long.Parse(clock[(separatorIndex + 1)..]));
    }

    private static string Format(long physical, long logical) => $"{physical:D19}-{logical:D10}";
}
