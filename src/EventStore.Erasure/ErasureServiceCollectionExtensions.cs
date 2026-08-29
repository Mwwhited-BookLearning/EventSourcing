using Amazon.KeyManagementService;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using EventStore.Abstractions;
using Google.Cloud.Kms.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Token;

namespace EventStore.Erasure;

public static class ErasureServiceCollectionExtensions
{
    // "Local" is always registered -- the safe, zero-external-dependency
    // default for dev and small/single-node deployments (ADR-057). Vault is
    // only registered when Erasure:Vault:Address is actually configured,
    // since constructing a VaultClient needs real connection info; a
    // deployment naming "HashiCorpVault" in ErasureOptions without
    // configuring Vault:Address gets a clear keyed-service-not-found error
    // at first use, not a silent fallback to Local.
    public static IServiceCollection AddErasure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ErasureOptions>(o => configuration.GetSection("Erasure").Bind(o));
        services.AddScoped<ErasureKeyService>();
        services.AddKeyedScoped<IErasureKeyStore, LocalErasureKeyStore>("Local");

        // Found this session while implementing ADR-096/097: PayloadEncryptor
        // (ADR-057's own encryption) was never registered anywhere -- every
        // real Host's PublishService constructor resolved its optional
        // PayloadEncryptor? param as null, so classified-field encryption
        // was inert in production, only ever exercised by test code that
        // constructs PayloadEncryptor directly. Fixed here, the one place
        // every Host already calls into for erasure wiring.
        services.AddScoped<PayloadEncryptor>();

        // ADR-096/097 -- searchable-index wiring, alongside crypto-shredding
        // since PayloadIndexer needs ErasureKeyService for PerEntity-scope
        // key derivation (see PayloadIndexer's own header comment).
        services.Configure<SearchIndexOptions>(o => configuration.GetSection("SearchIndex").Bind(o));
        services.AddScoped<SearchIndexKeyService>();
        services.AddKeyedScoped<ISearchIndexKeyStore, LocalSearchIndexKeyStore>("Local");
        services.AddScoped<PayloadIndexer>();
        services.AddScoped<IEncryptedPredicateEvaluator, AppTierEncryptedPredicateEvaluator>();

        var vaultAddress = configuration["Erasure:Vault:Address"];
        if (vaultAddress is not null)
        {
            var vaultToken = configuration["Erasure:Vault:Token"]!;
            IAuthMethodInfo authMethod = new TokenAuthMethodInfo(vaultToken);
            var vaultClient = new VaultClient(new VaultClientSettings(vaultAddress, authMethod));
            var backend = new HashiCorpVaultErasureKeyStore(vaultClient);
            services.AddKeyedSingleton<IErasureKeyStore>("HashiCorpVault", backend);
            // ADR-096 -- the same backend instance doubles as a
            // Shared-scope ISearchIndexKeyStore via CloudSearchIndexKeyStoreAdapter
            // (see that class's own header for why this reuses the erasure
            // backend's CreateKeyAsync/EncryptAsync rather than a fourth
            // bespoke SDK integration); a completely separate KeyReference/
            // key name from any entity's own DEK, tracked in its own
            // SearchIndexKey table, so there is no lifecycle coupling
            // despite sharing the underlying client/backend class.
            services.AddKeyedSingleton<ISearchIndexKeyStore>("HashiCorpVault", (_, _) => new CloudSearchIndexKeyStoreAdapter(backend));
        }

        // Same "only registered when its own connection info is actually
        // configured" posture as Vault above -- DefaultAzureCredential
        // resolves against whatever ambient identity the deployment
        // environment already provides (managed identity, az cli login,
        // environment variables), never a credential this config section
        // itself carries.
        var azureKeyVaultUri = configuration["Erasure:AzureKeyVault:VaultUri"];
        if (azureKeyVaultUri is not null)
        {
            var keyClient = new KeyClient(new Uri(azureKeyVaultUri), new DefaultAzureCredential());
            var backend = new AzureKeyVaultErasureKeyStore(keyClient);
            services.AddKeyedSingleton<IErasureKeyStore>("AzureKeyVault", backend);
            services.AddKeyedSingleton<ISearchIndexKeyStore>("AzureKeyVault", (_, _) => new CloudSearchIndexKeyStoreAdapter(backend));
        }

        // AWSSDK's own AmazonKeyManagementServiceClient resolves credentials
        // from the ambient AWS credential chain (environment variables,
        // ~/.aws/credentials, an EC2/ECS instance role) the same way
        // DefaultAzureCredential does for Azure above -- this config section
        // only ever supplies the region, never a credential.
        var awsRegion = configuration["Erasure:AwsKms:Region"];
        if (awsRegion is not null)
        {
            var kmsClient = new AmazonKeyManagementServiceClient(Amazon.RegionEndpoint.GetBySystemName(awsRegion));
            var backend = new AwsKmsErasureKeyStore(kmsClient);
            services.AddKeyedSingleton<IErasureKeyStore>("AwsKms", backend);
            services.AddKeyedSingleton<ISearchIndexKeyStore>("AwsKms", (_, _) => new CloudSearchIndexKeyStoreAdapter(backend));
        }

        // Google.Cloud.Kms.V1's own KeyManagementServiceClient.Create()
        // resolves Application Default Credentials the same way -- this
        // config section only ever supplies the resource-name components
        // (ProjectId/LocationId/KeyRingId), never a credential.
        var gcpProjectId = configuration["Erasure:GoogleCloudKms:ProjectId"];
        if (gcpProjectId is not null)
        {
            var locationId = configuration["Erasure:GoogleCloudKms:LocationId"]!;
            var keyRingId = configuration["Erasure:GoogleCloudKms:KeyRingId"]!;
            var kmsClient = KeyManagementServiceClient.Create();
            var backend = new GoogleCloudKmsErasureKeyStore(kmsClient, gcpProjectId, locationId, keyRingId);
            services.AddKeyedSingleton<IErasureKeyStore>("GoogleCloudKms", backend);
            services.AddKeyedSingleton<ISearchIndexKeyStore>("GoogleCloudKms", (_, _) => new CloudSearchIndexKeyStoreAdapter(backend));
        }

        return services;
    }
}
