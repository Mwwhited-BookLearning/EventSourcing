[← Libraries index](../README.md)

# SPIFFE/SPIRE (dotnet)

**What it's for:** [SPIFFE](https://spiffe.io/) (Secure Production
Identity Framework For Everyone) defines a workload identity standard —
every service gets a `spiffe://<trust-domain>/<path>` ID plus a
short-lived X.509-SVID (or JWT-SVID) certificate proving it, issued and
automatically rotated by [SPIRE](https://spiffe.io/docs/latest/spire-about/spire-concepts/)
(the reference implementation: a **SPIRE Server** per trust domain plus a
**SPIRE Agent** on every node, attesting a workload's identity from
node/process facts — not a shared secret a workload has to be handed and
protect). Two independent SPIRE deployments (different trust domains) can
**federate**, exchanging trust bundles so a workload in one can verify a
workload in the other directly, with no shared central IdP or root CA.

**Why bought, not built — with the same kind of honest caveat this
catalog already states for CEL's fragmented .NET ecosystem
([`cel-dotnet.md`](cel-dotnet.md)):** SPIRE Server/Agent themselves are
mature, CNCF-graduated infrastructure (Go binaries, not a NuGet package —
the same "external service, not an in-process library" shape
`hashicorp-vault.md` describes for Vault) — reimplementing X.509-SVID
issuance, rotation, and trust-bundle federation from scratch would be a
large, security-critical undertaking with no project-specific value.
**The .NET-side consumption story is thinner than most of this catalog,
stated plainly rather than glossed over**: there is no official,
first-party SPIFFE Workload API client for .NET (unlike `go-spiffe` for
Go); the closest is a small community package,
[`Spiffe`](https://www.nuget.org/packages/Spiffe) (v0.0.x, ~13k downloads
as of this writing) implementing the Workload API's gRPC contract. The
**lower-risk path this design actually relies on** is that the Workload
API is a documented, ordinary gRPC service (`workload.proto`) reachable
with any generic gRPC client (`Grpc.Net.Client`), and SPIRE Agent can also
just write the current X.509-SVID/trust bundle to a local file on rotation
— either way, the result is an ordinary `X509Certificate2` handed to
Kestrel's existing mTLS support, not a SPIFFE-specific object model a
service has to learn.

## General usage

```csharp
// Option A: SPIRE Agent writes the SVID + bundle to disk on rotation;
// the service just watches the file and reloads Kestrel's server certificate.
var svid = X509Certificate2.CreateFromPemFile(
    "/run/spire/svid.pem", "/run/spire/svid_key.pem");

builder.WebHost.ConfigureKestrel(o => o.ConfigureHttpsDefaults(https =>
{
    https.ServerCertificate = svid;
    https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
    https.ClientCertificateValidation = (cert, chain, errors) =>
        SpiffeIdValidator.IsTrustedPeer(cert, allowedTrustDomains: ["eventstore.site-a"]);
}));
```

```csharp
// Option B: fetch directly from the Workload API's gRPC endpoint
// (via a generic gRPC client, or the community `Spiffe` package).
var channel = GrpcChannel.ForAddress("unix:///run/spire/agent.sock");
var workloadClient = new SpiffeWorkloadApiClient(channel);
var svidResponse = await workloadClient.FetchX509SvidAsync();
```

## Where this project uses it

`ADR-048` — issues workload identity (a SPIFFE ID + X.509-SVID) for every
internal service (`EventStore.Router`, `.Fold`, `.GraphQL`, `.Sharding`,
`.PeerSync`, `.Streaming`, `.Attachments`); `ADR-033`'s peer-sync
authentication moves onto SPIFFE/SPIRE's cross-trust-domain federation for
mutual mTLS between independently-administered peer servers. `ADR-049`'s
[YARP](yarp.md) gateway hands off to this for internal
gateway-to-service calls, once external auth (`ADR-006`) has already
validated the caller.

## Links

- [spiffe.io](https://spiffe.io/)
- [github.com/spiffe/spire](https://github.com/spiffe/spire)
- [nuget.org/packages/Spiffe](https://www.nuget.org/packages/Spiffe) (community Workload API client, not first-party)
- [docs/comparisons/service-identity.md](../../comparisons/service-identity.md) — the full comparison against static OAuth2 client credentials and hand-rolled mTLS
