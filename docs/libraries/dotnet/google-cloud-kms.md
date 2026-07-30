[← Libraries index](../README.md)

# Google Cloud KMS (dotnet)

**What it's for:** Google Cloud's key-management service — creates and
protects cryptographic keys (optionally HSM-backed, via Cloud HSM) and
performs encrypt/decrypt/key-management operations against them via the
`Google.Cloud.Kms.V1` NuGet package's `KeyManagementServiceClient`.

**Why bought, not built:** the same reasoning as this catalog's other
named cloud KMS options for `ADR-057`'s `IErasureKeyStore` seam
([Azure Key Vault](azure-key-vault.md), [AWS KMS](aws-kms.md)) — a
deployment already running on Google Cloud gets key-management
infrastructure it doesn't have to operate itself, named here as one
concrete, real option rather than a hard framework dependency
(`ADR-041`'s secrets-management addendum).

## General usage

```csharp
var client = KeyManagementServiceClient.Create();
var keyName = new CryptoKeyName(projectId, "global", "eventstore-keyring", $"dek-{appId}-{entityId}");

// Encrypt a classified field value under this entity's key
EncryptResponse encrypted = client.Encrypt(keyName.ToString(), plaintextBytes);

// Erasure: destroy the key version, irreversible
client.DestroyCryptoKeyVersion(new CryptoKeyVersionName(
    projectId, "global", "eventstore-keyring", $"dek-{appId}-{entityId}", "1"));
```

## Where this project uses it

`ADR-057` — one of several named, concrete `IErasureKeyStore` backends
for the **cloud** tier, registered per `AppId` via ordinary configuration,
never a framework-wide hard-coded choice. `DestroyCryptoKeyVersion`'s
irreversible key destruction is the concrete mechanism behind an
`EntityErasureRequested` event's crypto-shredding for a tenant configured
to use Google Cloud KMS.

## Links

- [cloud.google.com/dotnet/docs/reference/Google.Cloud.Kms.V1/latest](https://cloud.google.com/dotnet/docs/reference/Google.Cloud.Kms.V1/latest)
- [github.com/googleapis/google-cloud-dotnet](https://github.com/googleapis/google-cloud-dotnet)
