using Amazon.KeyManagementService;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
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

        var vaultAddress = configuration["Erasure:Vault:Address"];
        if (vaultAddress is not null)
        {
            var vaultToken = configuration["Erasure:Vault:Token"]!;
            IAuthMethodInfo authMethod = new TokenAuthMethodInfo(vaultToken);
            var vaultClient = new VaultClient(new VaultClientSettings(vaultAddress, authMethod));
            services.AddKeyedSingleton<IErasureKeyStore>("HashiCorpVault", (_, _) => new HashiCorpVaultErasureKeyStore(vaultClient));
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
            services.AddKeyedSingleton<IErasureKeyStore>("AzureKeyVault", (_, _) => new AzureKeyVaultErasureKeyStore(keyClient));
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
            services.AddKeyedSingleton<IErasureKeyStore>("AwsKms", (_, _) => new AwsKmsErasureKeyStore(kmsClient));
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
            services.AddKeyedSingleton<IErasureKeyStore>("GoogleCloudKms", (_, _) => new GoogleCloudKmsErasureKeyStore(kmsClient, gcpProjectId, locationId, keyRingId));
        }

        return services;
    }
}
