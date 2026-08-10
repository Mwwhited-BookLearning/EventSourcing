using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace EventStore.Gateway;

// ADR-058 -- resolves the per-request partition key every rate limiter
// policy uses, so one AppId's own volume/connections/queries never affect
// another's (this ADR's own "one noisy tenant can't exhaust another's
// share" requirement). The Gateway (ADR-049) deliberately never validates
// the caller's JWT itself -- that's the Host's job -- so this can only
// ever be a best-effort PEEK, never an authenticated identity: first, an
// "appId" field read out of a buffered, still-forwarded JSON body (set by
// AppIdBufferingMiddleware for /publish and /follow, where the target
// AppId genuinely lives in the request body, not a claim); failing that, an
// UNVALIDATED "sub"/"client_id" claim read directly out of the JWT's own
// base64url payload segment (real authentication/signature verification
// still happens at the Host, exactly as before -- this read is for
// PARTITIONING traffic only, never a security decision); failing that, a
// fixed "anonymous" bucket shared by every caller nothing else could be
// resolved for.
public static class TenantPartitionKey
{
    public static string Resolve(HttpContext context)
    {
        if (context.Items[AppIdBufferingMiddleware.AppIdItemKey] is string appId)
            return appId;

        if (TryPeekJwtClaim(context.Request.Headers.Authorization.ToString(), out var claimValue))
            return claimValue!;

        return "anonymous";
    }

    private static bool TryPeekJwtClaim(string authorizationHeader, out string? value)
    {
        value = null;
        if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var token = authorizationHeader["Bearer ".Length..].Trim();
        var segments = token.Split('.');
        if (segments.Length < 2)
            return false;

        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(segments[1]));
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("client_id", out var clientId))
            {
                value = clientId.GetString();
                return value is not null;
            }
            if (document.RootElement.TryGetProperty("sub", out var sub))
            {
                value = sub.GetString();
                return value is not null;
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            // Not a well-formed JWT (or not a JWT at all) -- fall through to
            // the "anonymous" bucket rather than let a malformed
            // Authorization header take the Gateway down.
        }

        return false;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
