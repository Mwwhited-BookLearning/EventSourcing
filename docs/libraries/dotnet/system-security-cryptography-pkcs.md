[← Libraries index](../README.md)

# System.Security.Cryptography.Pkcs (dotnet)

**What it's for:** first-party BCL support for CMS/PKCS#7 (`SignedCms`)
and, since .NET 5, RFC 3161 Time-Stamp Protocol messages directly
(`Rfc3161TimestampRequest`, `Rfc3161TimestampToken`,
`Rfc3161TimestampTokenInfo`) — building/encoding a `TimeStampReq`,
parsing and cryptographically validating a TSA's `TimeStampToken`
response (hash/nonce/algorithm match, signature chain), with no
third-party crypto library required for the client side of RFC 3161.

**Why bought, not built:** ASN.1 DER encoding and CMS SignedData
signature verification are exactly the kind of low-level cryptographic
plumbing this design's own "prefer a real standard/library over a
bespoke mechanism" convention exists for — reimplementing RFC 3161's
wire format or CMS signature verification by hand would be pure risk for
zero benefit when the BCL already ships a verified implementation.
Verified directly against the real installed API surface this session
(a from-scratch request/issue/verify round trip, including the
`SigningCertificateV2` signed attribute RFC 3161/5035 mandates, run and
confirmed working before any production code depended on it) — not
assumed from documentation samples.

**Known anomaly, worth stating plainly:** the BCL ships the *reader*
side of a `TimeStampResp` publicly (`Rfc3161TimestampToken`/
`Rfc3161TimestampRequest.ProcessResponse`) but not a public *writer* —
there is no supported BCL API for *acting as* a TSA and issuing a token.
This is by design (issuing tokens is real TSA software's job, not a
client library's), but means this repo's own test-only fake TSA
(`tests/EventStore.IntegrationTests/TimestampingTestSupport.cs`) builds
the `TimeStampResp`/`PkiStatusInfo` wrapper by hand against the raw
ASN.1 (that specific pair of types is `internal` in the BCL), reusing
`SignedCms` for the actual CMS signing.

## General usage

```csharp
// Client side (EventStore.Timestamping/HttpTimestampAuthorityClient.cs)
var request = Rfc3161TimestampRequest.CreateFromHash(sha256Hash, HashAlgorithmName.SHA256);
var responseBytes = await PostToTsaAsync(request.Encode()); // Content-Type: application/timestamp-query
var token = request.ProcessResponse(responseBytes, out _);  // validates hash/nonce/signature chain
var tokenBytes = token.AsSignedCms().Encode();               // what gets persisted

// Later, independent verification -- no framework-specific mechanism needed:
Rfc3161TimestampToken.TryDecode(tokenBytes, out var decoded, out _);
decoded!.VerifySignatureForHash(sha256Hash, HashAlgorithmName.SHA256, out var signerCert, trustedTsaCerts);
```

## Where this project uses it

`ADR-086` — the sole basis for `EventStore.Timestamping`'s
`HttpTimestampAuthorityClient` (the default `ITimestampAuthorityClient`
implementation), timestamping `PublishService`'s `Signature.
RFC3161Timestamp` (over `SHA-256(ChainHash)`) and `LineageExportService`'s
`ExportManifest.Rfc3161Timestamp` (over `ManifestHash`'s own raw bytes).

## Links

- [learn.microsoft.com/dotnet/api/system.security.cryptography.pkcs.rfc3161timestamprequest](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.pkcs.rfc3161timestamprequest)
- [datatracker.ietf.org/doc/html/rfc3161](https://datatracker.ietf.org/doc/html/rfc3161)
