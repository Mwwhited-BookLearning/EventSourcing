[← Libraries index](../README.md)

# Azure Key Vault (dotnet)

**What it's for:** a cloud key-management service — stores and manages
cryptographic keys (RSA/EC, optionally HSM-backed) and exposes them
through the `Azure.Security.KeyVault.Keys`/`Azure.Identity` NuGet
packages, without the calling application ever holding raw key material
itself; a `CryptographyClient` performs wrap/unwrap and encrypt/decrypt
operations against a named key version.

**Why bought, not built:** correctly managing key lifecycle (rotation,
access policies/RBAC, HSM-backed protection, audit logging of every key
operation) is exactly the kind of security-critical infrastructure not
worth reimplementing — one of several concrete, real options this design
names for `ADR-057`'s `IErasureKeyStore` seam, not a hard dependency of
the framework itself (`ADR-041`'s secrets-management addendum: "this
framework itself adopts none of those providers as a hard dependency").

## General usage

```csharp
var client = new KeyClient(
    vaultUri: new Uri("https://eventstore-kv.vault.azure.net/"),
    credential: new DefaultAzureCredential());

// First classified field published for an (AppId, EntityId): mint a DEK-wrapping key
KeyVaultKey kek = await client.CreateKeyAsync($"dek-{appId}-{entityId}", KeyType.Rsa);

var cryptoClient = client.GetCryptographyClient(kek.Name, kek.Properties.Version);
WrapResult wrapped = await cryptoClient.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, dekBytes);

// Erasure: destroy the key, never touch StoredEvent.Payload
await client.StartDeleteKeyAsync(kek.Name);
await client.PurgeDeletedKeyAsync(kek.Name); // irreversible, per ADR-057
```

## Where this project uses it

`ADR-057` — one of several named, concrete `IErasureKeyStore` backends
for the **cloud** tier (alongside [AWS KMS](aws-kms.md) and
[Google Cloud KMS](google-cloud-kms.md)) — a deployment registers this
per `AppId` via ordinary configuration (`ADR-041`'s secrets addendum),
never as a framework-wide hard-coded choice. `PurgeDeletedKey`'s
irreversible key destruction is the concrete mechanism behind an
`EntityErasureRequested` event's crypto-shredding.

**Built and verified against the real SDK, 2026-08-12**
(`AzureKeyVaultErasureKeyStore`) — this sketch's own `WrapKeyAsync(dekBytes)`
call was already the right shape; the one thing it doesn't show is what
happens to `dekBytes` next: `EnvelopeAesGcm`'s own deterministic-nonce
AES-256-GCM encrypts the actual field value locally, never Key Vault's
own RSA-OAEP directly (that's size-limited and, more importantly, not
deterministic — encrypting the same plaintext twice produces different
ciphertext, which would break `ADR-011`'s publish-idempotency
comparison). `dek-{appId}-{entityId}:{base64WrappedDek}` is the real
`IErasureKeyStore.CreateKeyAsync` return value — the wrapped DEK travels
inside the opaque reference itself, so no extra persistence is needed
beyond what `EntityErasureKey.KeyReference` already stores.

## Links

- [learn.microsoft.com/dotnet/api/overview/azure/security.keyvault.keys-readme](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/security.keyvault.keys-readme)
- [nuget.org/packages/Azure.Security.KeyVault.Keys](https://www.nuget.org/packages/Azure.Security.KeyVault.Keys/)
