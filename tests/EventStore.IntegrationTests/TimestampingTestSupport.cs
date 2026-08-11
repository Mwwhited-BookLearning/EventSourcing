using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace EventStore.IntegrationTests;

// A real, minimal RFC 3161 TSA -- the same "DevIdp instead of a real
// external IdP" precedent this repo already established, applied to the
// one other external-trust-infrastructure seam this build introduces
// (ADR-086). Issues genuine, independently-verifiable TimeStampTokens
// (real RSA signature, real CMS SignedData, the mandatory
// SigningCertificateV2/ESSCertIDv2 signed attribute) using ONLY BCL types
// -- no third-party crypto library needed even for this test double, since
// .NET's own System.Security.Cryptography.Pkcs already exposes everything
// needed to both build a request/parse a token (the client side, used by
// EventStore.Timestamping's real production code) and, as proven here,
// to actually issue one (the TSA side, real production TSA software's
// job, never shipped by this framework itself). Every step of this
// exact encoding was cross-checked against a real request/issue/verify
// round trip run directly this session before being written into test
// code, not assumed from the RFC text alone.
internal static class TimestampingTestSupport
{
    public static async Task<(X509Certificate2 TsaCertificate, IHost Server)> StartFakeTsaAsync()
    {
        // Deliberately not disposed here (a test-only, process-lifetime leak,
        // never a production concern) -- CreateSelfSigned's resulting
        // X509Certificate2 keeps using this same RSA key on every later
        // signing call the fake TSA makes across the whole test class, and
        // an early Dispose (e.g. under a `using`) is not guaranteed safe to
        // sign with afterward on every platform.
        var rsa = RSA.Create(2048);
        var certRequest = new CertificateRequest("CN=Fake Test TSA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        // id-kp-timeStamping, MUST be critical and the certificate's only EKU
        // (RFC 3161 section 2.3) -- Rfc3161TimestampToken.TryDecode's own
        // CheckCertificate rejects the token otherwise.
        certRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.8") }, critical: true));
        var tsaCertificate = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(1));

        var hostBuilder = new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer();
            webBuilder.Configure(app => app.Run(async ctx =>
            {
                using var buffer = new MemoryStream();
                await ctx.Request.Body.CopyToAsync(buffer);
                var responseBytes = IssueToken(buffer.ToArray(), tsaCertificate);
                ctx.Response.ContentType = "application/timestamp-reply";
                await ctx.Response.Body.WriteAsync(responseBytes);
            }));
        });
        var server = await hostBuilder.StartAsync();
        return (tsaCertificate, server);
    }

    // RFC 3161 section 2.4.2's TimeStampToken ::= ContentInfo (id-signedData,
    // content TSTInfo) -- built directly via SignedCms (a plain CMS signer,
    // BCL) rather than any RFC-3161-specific "TSA" API, because the BCL
    // deliberately ships none (issuing a token is real TSA software's own
    // job, RFC 3161 says nothing about how; only the wire format is
    // standardized, which SignedCms already speaks).
    private static byte[] IssueToken(byte[] requestBytes, X509Certificate2 tsaCertificate)
    {
        if (!Rfc3161TimestampRequest.TryDecode(requestBytes, out var decodedRequest, out _))
            throw new CryptographicException("fake TSA received an undecodable TimeStampReq");

        var tokenInfo = new Rfc3161TimestampTokenInfo(
            policyId: new Oid("1.2.3.4.5"),
            hashAlgorithmId: decodedRequest.HashAlgorithmId,
            messageHash: decodedRequest.GetMessageHash(),
            serialNumber: Guid.NewGuid().ToByteArray(),
            timestamp: DateTimeOffset.UtcNow,
            accuracyInMicroseconds: null,
            isOrdering: false,
            nonce: decodedRequest.GetNonce(),
            timestampAuthorityName: null,
            extensions: null);

        var contentInfo = new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), tokenInfo.Encode());
        var signedCms = new SignedCms(contentInfo, detached: false);
        // requestSignerCertificates defaults to false on the client
        // (Rfc3161TimestampRequest.CreateFromHash) -- IncludeOption.None
        // matches that; Rfc3161TimestampRequest.ValidateResponse rejects a
        // response carrying certificates the caller never asked for.
        var signer = new CmsSigner(tsaCertificate) { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), IncludeOption = X509IncludeOption.None };
        signer.SignedAttributes.Add(BuildSigningCertificateV2Attribute(tsaCertificate));
        signedCms.ComputeSignature(signer);
        var tokenBytes = signedCms.Encode();

        // TimeStampResp ::= SEQUENCE { status PKIStatusInfo, timeStampToken
        // TimeStampToken OPTIONAL } / PKIStatusInfo ::= SEQUENCE { status
        // INTEGER } -- 0 = granted. No public BCL type builds this (only
        // the reader half is public, Rfc3161TimeStampResp is internal),
        // so it's hand-written directly against the ASN.1, matching this
        // repo's own precedent of reading real framework source rather
        // than guessing when a doc sample doesn't exist (docs/06-solution-
        // structure.md's "concept accurate, exact wiring unverified" note
        // has caught this class of gap before).
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
                writer.WriteInteger(0);
            writer.WriteEncodedValue(tokenBytes);
        }
        return writer.Encode();
    }

    // SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF ESSCertIDv2 },
    // ESSCertIDv2 ::= SEQUENCE { hashAlgorithm AlgorithmIdentifier DEFAULT
    // {sha256}, certHash OCTET STRING, issuerSerial IssuerSerial OPTIONAL }
    // -- RFC 5035/2634's mandatory signed attribute identifying the TSA's
    // own signing certificate; Rfc3161TimestampToken.TryDecode's
    // TryGetCertIds requires it (or the older ESSCertID/SigningCertificate)
    // and fails the whole decode without it.
    private static Pkcs9AttributeObject BuildSigningCertificateV2Attribute(X509Certificate2 tsaCertificate)
    {
        var certHash = SHA256.HashData(tsaCertificate.RawData);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence())
            writer.WriteOctetString(certHash);
        return new Pkcs9AttributeObject(new Oid("1.2.840.113549.1.9.16.2.47"), writer.Encode());
    }
}
