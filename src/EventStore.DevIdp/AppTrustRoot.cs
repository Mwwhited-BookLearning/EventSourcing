namespace EventStore.DevIdp;

// ADR-044 -- resolves the one thing the UCAN spec itself leaves out-of-band
// (which DID counts as a root of trust for a given AppId's own custom
// permission namespace). `docs/data/schema-registry.md` catalogs this as a
// core-engine entity; built here, inside DevIdp, instead -- see this item's
// own Built-scope note in 08-build-plan.md: nothing in this design gives
// DevIdp (a separate process/deployment) a live dependency on any Host's
// own EventStoreContext database, and every consumer of this table is
// DevIdp's own token-exchange logic, never core-engine Publish/Follow/
// GraphQL code. `IssuerDid` is, in this implementation, the RFC 7638 JWK
// thumbprint of the trusted issuer's own EC P-256 keypair (the same
// keypair every seeded client already holds, ADR-017) -- an honest stand-in
// for a real W3C `did:key`, not full DID document resolution.
public class AppTrustRoot
{
    public string AppId { get; set; } = default!;
    public string IssuerDid { get; set; } = default!;
    public string? Description { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
}
