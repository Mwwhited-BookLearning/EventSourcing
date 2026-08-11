namespace EventStore.Spiffe;

// spiffe://<trust-domain>/<path> -- https://github.com/spiffe/spiffe/blob/main/standards/SPIFFE-ID.md.
// No partially-valid instance exists; construction always goes through
// TryParse/Parse.
public sealed class SpiffeId
{
    public string TrustDomain { get; }
    public string Path { get; }

    private SpiffeId(string trustDomain, string path)
    {
        TrustDomain = trustDomain;
        Path = path;
    }

    public static bool TryParse(string value, out SpiffeId? spiffeId)
    {
        spiffeId = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "spiffe")
            return false;
        if (string.IsNullOrEmpty(uri.Host))
            return false;

        spiffeId = new SpiffeId(uri.Host, uri.AbsolutePath);
        return true;
    }

    public static SpiffeId Parse(string value) =>
        TryParse(value, out var id) ? id! : throw new FormatException($"not a valid SPIFFE ID: {value}");

    public override string ToString() => $"spiffe://{TrustDomain}{Path}";

    public override bool Equals(object? obj) =>
        obj is SpiffeId other && TrustDomain == other.TrustDomain && Path == other.Path;

    public override int GetHashCode() => HashCode.Combine(TrustDomain, Path);
}
