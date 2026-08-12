[← Libraries index](../README.md)

# AWS KMS (dotnet)

**What it's for:** Amazon's cloud key-management service — creates and
protects encryption keys (optionally HSM-backed) and performs
generate-data-key/encrypt/decrypt operations against them via the
`AWSSDK.KeyManagementService` NuGet package, without the calling
application handling raw key material directly.

**Why bought, not built:** the same reasoning as every other cloud KMS
this catalog names for `ADR-057`'s `IErasureKeyStore` seam
([Azure Key Vault](azure-key-vault.md), [Google Cloud KMS](google-cloud-kms.md))
— key lifecycle management is security-critical infrastructure a cloud
platform already operates correctly at scale, and this design names it as
one concrete, real option rather than a hard dependency (`ADR-041`'s
secrets-management addendum).

## General usage

```csharp
var kms = new AmazonKeyManagementServiceClient();

// First classified field published for an (AppId, EntityId): generate a DEK
var dataKey = await kms.GenerateDataKeyAsync(new GenerateDataKeyRequest
{
    KeyId = $"alias/eventstore-{appId}",
    KeySpec = DataKeySpec.AES_256
});
// dataKey.Plaintext encrypts the payload field; dataKey.CiphertextBlob is
// what gets stored as the wrapped DEK (EntityErasureKey, ADR-057)

// Erasure: schedule the key for deletion (irreversible once the window elapses)
await kms.ScheduleKeyDeletionAsync(new ScheduleKeyDeletionRequest
{
    KeyId = $"alias/eventstore-{appId}-{entityId}",
    PendingWindowInDays = 7
});
```

## Where this project uses it

`ADR-057` — one of several named, concrete `IErasureKeyStore` backends
for the **cloud** tier, registered per `AppId` via ordinary configuration,
never a framework-wide hard-coded choice. `ScheduleKeyDeletion`'s
irreversible (after its pending window) key destruction is the concrete
mechanism behind an `EntityErasureRequested` event's crypto-shredding for
a tenant configured to use AWS KMS.

**Corrected, 2026-08-12** (`AwsKmsErasureKeyStore`, built and verified
against the real SDK): the sketch above is internally inconsistent — it
generates the data key against a per-`AppId` alias (line ~27) but then
schedules deletion of a per-`AppId`-*and*-`entityId` alias that was never
created (line ~36). A single CMK shared across every entity for an
`AppId` (the sketch's own original intent) genuinely cannot support
per-entity erasure: destroying that ONE shared CMK to erase one entity
would also erase every OTHER entity still wrapped under it, and KMS has
no way to selectively revoke a shared CMK's ability to decrypt one
entity's already-issued ciphertext. The real implementation creates one
**customer-managed key (CMK) per entity** instead (`kms.CreateKeyAsync`,
not an alias) — a real, accepted cost specific to this backend (AWS
account-level CMK quotas, unlike Vault/Azure/GCP's cheaper named-key-
per-entity model) — so `DestroyKeyAsync` means exactly what it means for
every other backend. `EnvelopeAesGcm`'s own deterministic-nonce AES-256-
GCM encrypts the actual field value locally (same reasoning as Azure Key
Vault's own correction note) — KMS's native `Encrypt` is neither
size-unbounded nor deterministic across calls.

## Links

- [nuget.org/packages/AWSSDK.KeyManagementService](https://www.nuget.org/packages/AWSSDK.KeyManagementService)
- [github.com/aws/aws-sdk-net](https://github.com/aws/aws-sdk-net)
