namespace EventStore.Abstractions;

// ADR-086 -- a pluggable RFC 3161 Time Stamping Authority dependency,
// registered per this repo's ordinary composition-root DI convention
// (ADR-059), the same "external trust infrastructure is swappable" shape
// IErasureKeyStore already establishes. No default implementation is
// shipped here -- a deployment registers a public TSA or an internally-
// operated one. Returns the encoded RFC 3161 TimeStampToken bytes (a
// signed CMS ContentInfo, RFC 3161 section 2.4.2) over the given hash;
// verification needs no new mechanism (ADR-086's own Decision text) --
// System.Security.Cryptography.Pkcs.Rfc3161TimestampToken.TryDecode/
// VerifySignatureForHash, already part of the BCL, reads these bytes back.
public interface ITimestampAuthorityClient
{
    Task<byte[]> TimestampHashAsync(byte[] sha256Hash, CancellationToken ct = default);
}
