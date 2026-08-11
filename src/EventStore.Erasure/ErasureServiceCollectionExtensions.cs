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

        return services;
    }
}
