namespace EventStore.Inbox;

// ADR-033 -- every event stamps the site it originated at. A real
// deployment configures this per-site (e.g. "site-a", "site-b"); a
// single-site deployment (every test/scenario predating this item) never
// needs to set it explicitly.
public class OriginIdOptions
{
    public const string Default = "local";

    public string OriginId { get; set; } = Default;
}
