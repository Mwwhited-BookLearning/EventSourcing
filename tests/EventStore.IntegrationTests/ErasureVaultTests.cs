using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using EventStore.Erasure;
using EventStore.Follow.Api;
using EventStore.Inbox;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.Router;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Token;

namespace EventStore.IntegrationTests;

// ADR-057's own exit-criteria example: "two different AppIds configured with
// different backends (Local vs. HashiCorpVault) both working in the same
// deployment." Against a REAL Vault dev-mode server, not a mock -- the whole
// point of HashiCorpVaultErasureKeyStore is that its API surface was verified
// by reflection against the actual installed package (see that class's own
// comment); a mock would only re-assert what the reflection probe already
// confirmed, not that this store's calls actually round-trip against Vault's
// real HTTP API.
[TestClass]
public class ErasureVaultTests
{
    private const string RootToken = "vault-erasure-test-root";
    private static IContainer _container = default!;
    private static string _vaultAddress = default!;
    private static string _dbPath = default!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _container = new ContainerBuilder()
            .WithImage("hashicorp/vault:1.19")
            .WithEnvironment("VAULT_DEV_ROOT_TOKEN_ID", RootToken)
            .WithPortBinding(8200, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPath("/v1/sys/health").ForPort(8200)))
            .Build();
        await _container.StartAsync();
        _vaultAddress = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8200)}";

        await HashiCorpVaultErasureKeyStore.EnsureTransitEngineMountedAsync(CreateVaultClient());

        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-erasure-vault-{Guid.NewGuid():N}.db");
        using var db = CreateContext();
        db.Database.Migrate();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _container.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static IVaultClient CreateVaultClient()
    {
        IAuthMethodInfo authMethod = new TokenAuthMethodInfo(RootToken);
        return new VaultClient(new VaultClientSettings(_vaultAddress, authMethod));
    }

    private static EventStoreContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        return new EventStoreContext(options, new SqliteJsonPathTranslator());
    }

    [TestMethod]
    public async Task TheHashiCorpVaultBackendRoundTripsAndDestroysAKeyAgainstARealVaultServer()
    {
        var backend = new HashiCorpVaultErasureKeyStore(CreateVaultClient());
        var keyReference = await backend.CreateKeyAsync("vault-probe-app", "vault-probe-app:thing:1");
        var plaintext = "genuinely real Vault round trip"u8.ToArray();

        var ciphertext = await backend.EncryptAsync(keyReference, plaintext);
        CollectionAssert.AreNotEqual(plaintext, ciphertext, "Vault's own ciphertext must never equal the plaintext bytes");

        var decrypted = await backend.DecryptAsync(keyReference, ciphertext);
        CollectionAssert.AreEqual(plaintext, decrypted);

        await backend.DestroyKeyAsync(keyReference);
        Assert.IsNull(await backend.DecryptAsync(keyReference, ciphertext), "IErasureKeyStore's own contract: null means erased");
    }

    [TestMethod]
    public async Task TwoAppIdsConfiguredToDifferentBackendsBothWorkCorrectlyInTheSameDeployment()
    {
        const string localAppId = "erasure-vault-local-app";
        const string vaultAppId = "erasure-vault-remote-app";
        const string typeName = "RecordRecordedErasureVault";

        using var db = CreateContext();
        var registry = new SchemaRegistryService(db, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Erasure:Vault:Address"] = _vaultAddress,
            ["Erasure:Vault:Token"] = RootToken,
            ["Erasure:DefaultBackend"] = "Local",
            [$"Erasure:BackendByAppId:{vaultAppId}"] = "HashiCorpVault",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton(registry);
        services.AddErasure(configuration);
        services.AddMasking(new Dictionary<string, string>());
        var provider = services.BuildServiceProvider();
        var erasureKeyService = provider.GetRequiredService<ErasureKeyService>();
        var payloadMasker = provider.GetRequiredService<IPayloadMasker>();
        var encryptor = new PayloadEncryptor(erasureKeyService);

        var publish = new PublishService(db, registry, new SqliteUniqueConstraintViolationDetector(), encryptor);
        var upcastChain = UpcastingTestSupport.CreateChain();
        var follow = new FollowService(db, registry, new EventTailReader(db, registry, payloadMasker, upcastChain, UpcastingTestSupport.CreateDowncastChain()));

        foreach (var appId in new[] { localAppId, vaultAppId })
        {
            await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
                AppId: appId,
                JsonSchema: """
                    { "type": "object", "properties": {
                        "RecordId": { "type": "string" },
                        "Secret": { "type": "string", "x-masking": {
                            "strategy": "FixedValue", "requiredClaim": "clearance:secret", "regulatoryClassification": "PII" } }
                      }, "required": ["RecordId", "Secret"] }
                    """,
                FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.RecordId", ParentValidationMode: "Permissive",
                RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
            var publishResult = await publish.PublishAsync(typeName,
                new PublishEventRequest(appId, 1, """{ "RecordId": "rec-1", "Secret": "top-secret-value" }""", null, null, null), TestClaimsPrincipal.None);
            Assert.IsInstanceOfType<PublishResult.Accepted>(publishResult);
        }

        await RouterWorker.RunOnceAsync(db, registry, upcastChain, erasureKeyService);

        foreach (var appId in new[] { localAppId, vaultAppId })
        {
            using var cts = new CancellationTokenSource();
            var connected = (FollowResult.Connected)await follow.ConnectAsync(
                typeName, new FollowRequest(appId, Filter: null, Mode: "Replay", FromSequenceNumber: 0), TestClaimsPrincipal.With("clearance:secret"), cts.Token);
            await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);
            Assert.IsTrue(await enumerator.MoveNextAsync());
            var payload = enumerator.Current.MaskedPayload!;
            Assert.AreEqual("top-secret-value", payload["Secret"]!["value"]!.GetValue<string>(), $"appId {appId} failed to decrypt through its own configured backend");
            cts.Cancel();
        }

        // Confirms the two AppIds actually used DIFFERENT backends, not that
        // both happened to work by accident: the Vault-backed key must never
        // exist in this deployment's own LocalErasureKeyMaterial table.
        var vaultKey = await db.EntityErasureKeys.AsNoTracking().SingleAsync(k => k.EntityId == $"{vaultAppId}:{typeName.ToLowerInvariant()}:rec-1");
        Assert.AreEqual("HashiCorpVault", vaultKey.BackendName);
        Assert.IsFalse(await db.LocalErasureKeyMaterials.AnyAsync(m => m.KeyReference == vaultKey.KeyReference));

        var localKey = await db.EntityErasureKeys.AsNoTracking().SingleAsync(k => k.EntityId == $"{localAppId}:{typeName.ToLowerInvariant()}:rec-1");
        Assert.AreEqual("Local", localKey.BackendName);
    }
}
