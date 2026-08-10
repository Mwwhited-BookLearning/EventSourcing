using System.Security.Cryptography;
using System.Text;

namespace EventStore.Webhooks;

// ADR-060 -- Standard Webhooks' own header/signing shape, used as
// specified rather than reinvented: webhook-id (delivery identifier,
// doubles as the receiver's own idempotency key), webhook-timestamp, and
// webhook-signature = HMAC-SHA256("{id}.{timestamp}.{payload}", secret).
public static class WebhookSigner
{
    public static (string WebhookId, string Timestamp, string Signature) Sign(string payload, string signingSecret, Guid webhookId, DateTimeOffset timestamp)
    {
        var id = webhookId.ToString();
        var ts = timestamp.ToUnixTimeSeconds().ToString();
        var signature = ComputeSignature(id, ts, payload, signingSecret);
        return (id, ts, signature);
    }

    private static string ComputeSignature(string id, string timestamp, string payload, string signingSecret)
    {
        var toSign = $"{id}.{timestamp}.{payload}";
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingSecret), Encoding.UTF8.GetBytes(toSign));
        return $"v1,{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string payload, string signingSecret, string webhookId, string timestamp, string signatureHeader)
    {
        var expected = ComputeSignature(webhookId, timestamp, payload, signingSecret);
        return signatureHeader.Split(' ').Any(candidate => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(expected)));
    }
}
