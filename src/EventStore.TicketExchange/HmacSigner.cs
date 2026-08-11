using System.Security.Cryptography;
using System.Text;

namespace EventStore.TicketExchange;

// ADR-040 -- "the same HMAC signed-URL convention CDNs use for token-
// authenticated content" (Google Cloud CDN/AWS CloudFront signed URLs,
// BunnyCDN/nginx secure_link). Shared by both sides on purpose: the
// requesting party (a caller, or a test standing in for one) computes
// `sig`, and EventStore.DevIdp recomputes the identical value at
// introspection time -- one implementation, not two independently-written
// HMAC calls that could silently drift apart.
public static class HmacSigner
{
    public static string Sign(string ticket, string secret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(ticket));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
