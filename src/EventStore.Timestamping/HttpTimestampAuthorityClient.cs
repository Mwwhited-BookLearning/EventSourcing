using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using EventStore.Abstractions;
using Microsoft.Extensions.Options;

namespace EventStore.Timestamping;

// ADR-086 -- RFC 3161 section 3.4 (HTTP protocol): POST the DER-encoded
// TimeStampReq with Content-Type "application/timestamp-query," expect a
// DER-encoded TimeStampResp back with Content-Type "application/
// timestamp-reply." System.Security.Cryptography.Pkcs.Rfc3161TimestampRequest
// (BCL, part of the shared framework since .NET 5) builds the request and
// parses/validates the response -- no third-party RFC 3161 library needed
// for the client half; "verification needs no new mechanism" (ADR-086's
// own Decision text) holds for request/response handling too, verified
// directly against the real API surface this session (a from-scratch
// request/fake-TSA-response/verify round trip), not assumed from docs.
public class HttpTimestampAuthorityClient(HttpClient httpClient, IOptions<TimestampingOptions> options) : ITimestampAuthorityClient
{
    private static readonly MediaTypeHeaderValue TimestampQueryContentType = new("application/timestamp-query");

    public async Task<byte[]> TimestampHashAsync(byte[] sha256Hash, CancellationToken ct = default)
    {
        var tsaUrl = options.Value.TsaUrl
            ?? throw new InvalidOperationException("Timestamping:TsaUrl is not configured -- ITimestampAuthorityClient has nothing to call.");

        var request = Rfc3161TimestampRequest.CreateFromHash(sha256Hash, HashAlgorithmName.SHA256);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, tsaUrl)
        {
            Content = new ByteArrayContent(request.Encode()) { Headers = { ContentType = TimestampQueryContentType } },
        };
        using var httpResponse = await httpClient.SendAsync(httpRequest, ct);
        httpResponse.EnsureSuccessStatusCode();
        var responseBytes = await httpResponse.Content.ReadAsByteArrayAsync(ct);

        // ProcessResponse validates the hash/nonce/algorithm match and the
        // TSA's own signature chain (Rfc3161TimestampRequest.ValidateResponse)
        // before returning -- a mismatched or malformed reply throws here,
        // never silently accepted.
        var token = request.ProcessResponse(responseBytes, out _);
        return token.AsSignedCms().Encode();
    }
}
