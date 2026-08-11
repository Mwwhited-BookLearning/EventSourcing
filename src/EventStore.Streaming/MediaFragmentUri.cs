using System.Globalization;

namespace EventStore.Streaming;

// ADR-031 -- deep-linking adopts the W3C Media Fragments URI spec's
// temporal fragment syntax (https://www.w3.org/TR/media-frags/#naming-time)
// rather than a bespoke query-parameter scheme: "#t=10,20" (a half-open
// begin/end interval, in seconds, end optional) or the npt-prefixed form
// "#t=npt:10,20" -- both legal per that spec. This is the same *shape*
// TelemetryPointer's {FromTimestamp, ToTimestamp?} already has, adopted for
// the URI form specifically so a deep-link and an internal TelemetryPointer
// are trivially interconvertible.
public static class MediaFragmentUri
{
    public static bool TryParse(string fragment, out double beginSeconds, out double? endSeconds)
    {
        beginSeconds = 0;
        endSeconds = null;

        var text = fragment.TrimStart('#');
        if (!text.StartsWith("t=", StringComparison.OrdinalIgnoreCase))
            return false;

        var range = text[2..];
        if (range.StartsWith("npt:", StringComparison.OrdinalIgnoreCase))
            range = range[4..];

        var parts = range.Split(',');
        if (parts.Length is not (1 or 2))
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out beginSeconds))
            return false;

        if (parts.Length == 2)
        {
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var end))
                return false;
            endSeconds = end;
        }

        return true;
    }
}
