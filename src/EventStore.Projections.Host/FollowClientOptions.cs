namespace EventStore.Projections.Host;

public class FollowClientOptions
{
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string Scope { get; set; } = "events:follow";
}
