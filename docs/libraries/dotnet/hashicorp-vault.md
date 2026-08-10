[← Libraries index](../README.md)

# HashiCorp Vault (dotnet client: VaultSharp)

**What it's for:** HashiCorp Vault is a self-hostable secrets/key
management server — run entirely on-prem or in a private data center, not
cloud-only — with a `transit` secrets engine that performs key generation
and encrypt/decrypt operations without ever handing raw key material to
the caller. [`VaultSharp`](https://www.nuget.org/packages/VaultSharp) is
the comprehensive, actively-maintained cross-platform .NET client for it.

**Why bought, not built:** the same key-management reasoning this catalog
already applies to the cloud options for `ADR-057`'s `IErasureKeyStore`
seam ([Azure Key Vault](azure-key-vault.md), [AWS KMS](aws-kms.md),
[Google Cloud KMS](google-cloud-kms.md)) — but Vault is specifically named
for the **on-prem/self-hosted** tier, the option a tenant with a real
data-sovereignty requirement needs (a healthcare tenant keeping its keys
inside its own data center, composing with `ADR-061`'s region-pinning,
while a different tenant in the same deployment uses a cloud KMS).
[HashiCorp's own GDPR-compliant event-sourcing write-up](https://www.hashicorp.com/en/resources/gdpr-compliant-event-sourcing-with-hashicorp-vault)
independently arrives at this exact per-subject-key shape, not a novel
application of the tool.

## General usage

```csharp
IAuthMethodInfo authMethod = new TokenAuthMethodInfo(vaultToken);
var settings = new VaultClientSettings("https://vault.internal:8200", authMethod);
IVaultClient vaultClient = new VaultClient(settings);

// transit engine: encrypt a classified field value under this entity's key,
// without VaultSharp/the caller ever seeing the raw key material
var encrypted = await vaultClient.V1.Secrets.Transit.EncryptAsync(
    $"dek-{appId}-{entityId}", new EncryptRequestOptions { Base64EncodedPlainText = plaintextBase64 }, "transit");

// Erasure: deletion must be explicitly allowed on the key first (a Vault
// safety default), then deleted -- irreversible once purged. Verified
// against the installed package via reflection while building
// EventStore.Erasure.HashiCorpVaultErasureKeyStore, not assumed from this
// snippet's own earlier draft: the delete method lives on
// ITransitSecretsEngine, not V1.System, which this snippet originally (and
// incorrectly) named.
var keyName = $"dek-{appId}-{entityId}";
await vaultClient.V1.Secrets.Transit.UpdateEncryptionKeyConfigAsync(
    keyName, new UpdateKeyRequestOptions { DeletionAllowed = true }, "transit");
await vaultClient.V1.Secrets.Transit.DeleteEncryptionKeyAsync(keyName, "transit");
```

## Where this project uses it

`ADR-057` — one of several named, concrete `IErasureKeyStore` backends,
specifically the **on-prem/self-hosted** tier, registered per `AppId` via
ordinary configuration (`ADR-041`'s secrets addendum already names
HashiCorp Vault as one of the `Microsoft.Extensions.Configuration`-
compatible provider options for secrets generally, independent of this
specific erasure-key use).

## Links

- [nuget.org/packages/VaultSharp](https://www.nuget.org/packages/VaultSharp)
- [github.com/rajanadar/VaultSharp](https://github.com/rajanadar/VaultSharp)
- [vaultproject.io](https://www.vaultproject.io/)
