using EventStore.Erasure;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventStore.UnitTests;

// ADR-096 -- CloudSearchIndexKeyStoreAdapter wraps any IErasureKeyStore
// into an ISearchIndexKeyStore via a key-derivation trick, so its
// correctness is provider-agnostic: exercised here against
// LocalErasureKeyStore (no real cloud credentials needed) rather than
// against all four real cloud SDKs, since the adapter's own logic never
// branches on which backend it wraps.
[TestClass]
public class CloudSearchIndexKeyStoreAdapterTests
{
    private static EventStoreContext CreateContext(string dbPath)
    {
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        return new EventStoreContext(options, new SqliteJsonPathTranslator());
    }

    [TestMethod]
    public async Task ComputeHmacIsDeterministicForTheSameKeyAndData()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-cloud-search-index-adapter-{Guid.NewGuid():N}.db");
        try
        {
            using var db = CreateContext(dbPath);
            await db.Database.MigrateAsync();

            var backend = new LocalErasureKeyStore(db);
            var adapter = new CloudSearchIndexKeyStoreAdapter(backend);
            var keyReference = await adapter.CreateKeyAsync("demo-app", "orderplaced:$.Email");

            var mac1 = await adapter.ComputeHmacAsync(keyReference, "alice@example.com"u8.ToArray());
            var mac2 = await adapter.ComputeHmacAsync(keyReference, "alice@example.com"u8.ToArray());
            var mac3 = await adapter.ComputeHmacAsync(keyReference, "bob@example.com"u8.ToArray());

            CollectionAssert.AreEqual(mac1, mac2);
            CollectionAssert.AreNotEqual(mac1, mac3);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public async Task DifferentKeyReferencesProduceDifferentHmacsForTheSameData()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-cloud-search-index-adapter-{Guid.NewGuid():N}.db");
        try
        {
            using var db = CreateContext(dbPath);
            await db.Database.MigrateAsync();

            var backend = new LocalErasureKeyStore(db);
            var adapter = new CloudSearchIndexKeyStoreAdapter(backend);
            var keyA = await adapter.CreateKeyAsync("demo-app", "orderplaced:$.Email");
            var keyB = await adapter.CreateKeyAsync("demo-app", "customerregistered:$.Email");

            var macA = await adapter.ComputeHmacAsync(keyA, "alice@example.com"u8.ToArray());
            var macB = await adapter.ComputeHmacAsync(keyB, "alice@example.com"u8.ToArray());

            CollectionAssert.AreNotEqual(macA, macB);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public async Task ComputeHmacFailsOnceTheUnderlyingKeyIsDestroyed()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-cloud-search-index-adapter-{Guid.NewGuid():N}.db");
        try
        {
            using var db = CreateContext(dbPath);
            await db.Database.MigrateAsync();

            var backend = new LocalErasureKeyStore(db);
            var adapter = new CloudSearchIndexKeyStoreAdapter(backend);
            var keyReference = await adapter.CreateKeyAsync("demo-app", "orderplaced:$.Email");
            await adapter.ComputeHmacAsync(keyReference, "alice@example.com"u8.ToArray()); // works before destruction

            await backend.DestroyKeyAsync(keyReference);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                adapter.ComputeHmacAsync(keyReference, "alice@example.com"u8.ToArray()));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
