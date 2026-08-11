namespace EventStore.Replication;

// ADR-061 -- "every configured peer gains a Region tag... a small,
// deployment-time configuration addition, not a new discovery mechanism."
// A single-site/test deployment never needs to set this explicitly --
// null Region means this site simply carries no residency tag of its own,
// so no AppId's AllowedRegions constraint can ever admit syncing TO it
// specifically by name (the conservative default, matching ADR-061's own
// "residency wins" priority when in doubt).
public class RegionOptions
{
    public string? Region { get; set; }
}
