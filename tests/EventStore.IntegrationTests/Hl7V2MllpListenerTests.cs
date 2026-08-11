using System.Net;
using System.Net.Sockets;
using System.Text;
using EventStore.Inbox;
using EventStore.Interchange;
using EventStore.Interchange.Abstractions;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Sqlite;
using EventStore.SchemaRegistry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "Bulk Ingestion & External Interchange-Format Adapters" (docs/08-build-
// plan.md, ADR-072) -- the one scenario that genuinely needs a real
// socket: HL7v2's real transport is MLLP over TCP, matching Google
// Cloud's own published MLLP adapter, not something a direct-method-call
// test could prove. Runs Hl7V2MllpListener as a real BackgroundService
// bound to an OS-assigned port (Hl7V2MllpOptions.Port = 0), connects a
// real TcpClient, and speaks the actual MLLP start/end-block framing on
// both sides.
[TestClass]
public class Hl7V2MllpListenerTests
{
    private const byte StartBlock = 0x0B;
    private const byte EndBlock = 0x1C;
    private const byte CarriageReturn = 0x0D;

    private string _dbPath = default!;

    [TestInitialize]
    public async Task TestInit()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"eventstore-mllp-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options;
        await using var db = new EventStoreContext(options, new SqliteJsonPathTranslator());
        await db.Database.MigrateAsync();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private EventStoreContext CreateContext() => new(
        new DbContextOptionsBuilder<EventStoreContext>()
            .UseSqlite($"Data Source={_dbPath}", x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Sqlite"))
            .Options,
        new SqliteJsonPathTranslator());

    [TestMethod]
    public async Task AnAdtA01MessageSentOverRealMllpTcpIsParsedPublishedAndAcknowledged()
    {
        const string appId = "mllp-demo-1";
        await using (var setupDb = CreateContext())
        {
            var registry = new SchemaRegistryService(setupDb, new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator());
            await registry.RegisterAsync("PatientAdmitted", new RegisterEventTypeRequest(
                AppId: appId, JsonSchema: """{ "type": "object", "properties": { "PatientId": { "type": "string" }, "LastName": { "type": "string" }, "FirstName": { "type": "string" } }, "required": ["PatientId"] }""",
                FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.PatientId", ParentValidationMode: "Permissive",
                RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => CreateContext());
        services.AddScoped(sp => new SchemaRegistryService(sp.GetRequiredService<EventStoreContext>(), new SqliteFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), UpcastingTestSupport.CreateEvaluator()));
        services.AddScoped(sp => new PublishService(sp.GetRequiredService<EventStoreContext>(), sp.GetRequiredService<SchemaRegistryService>(), new SqliteUniqueConstraintViolationDetector()));
        services.AddKeyedScoped<IInterchangeFormatAdapter, Hl7V2Adapter>("Hl7V2");
        var provider = services.BuildServiceProvider();

        var mllpOptions = Options.Create(new Hl7V2MllpOptions { Enabled = true, Port = 0, AppId = appId });
        var listener = new Hl7V2MllpListener(provider.GetRequiredService<IServiceScopeFactory>(), mllpOptions, NullLogger<Hl7V2MllpListener>.Instance);
        using var cts = new CancellationTokenSource();
        await listener.StartAsync(cts.Token);
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (listener.BoundPort is null && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            Assert.IsNotNull(listener.BoundPort, "the listener bound to an OS-assigned port");

            var message = "MSH|^~\\&|EMR|Hospital|EventStore|EventStore|20260810120000||ADT^A01|MSG00001|P|2.3\r" +
                          "EVN|A01|20260810120000\r" +
                          "PID|1||mllp-pat-1^^^MRN||DOE^JOHN||19800101|M\r";

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.BoundPort!.Value, cts.Token);
            await using var stream = client.GetStream();
            await WriteMllpAsync(stream, message, cts.Token);
            var ack = await ReadMllpAsync(stream, cts.Token);

            Assert.IsTrue(ack.Contains("MSA|AA|"), $"expected a real HL7v2 application-accept ACK, got: {ack}");

            await using var db = CreateContext();
            var stored = await db.Events.AsNoTracking().SingleAsync(e => e.AppId == appId && e.EventType == "patientadmitted");
            Assert.IsTrue(stored.Payload.Contains("mllp-pat-1"));
            // ADR-035/042/072 -- an MLLP-sourced admit starts below "accepted"
            // (non-authoritative capture), never the ordinary-publish default.
            Assert.AreNotEqual("accepted", stored.AuthorityStatus);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WriteMllpAsync(NetworkStream stream, string message, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        await stream.WriteAsync(new[] { StartBlock }, ct);
        await stream.WriteAsync(payload, ct);
        await stream.WriteAsync(new[] { EndBlock, CarriageReturn }, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<string> ReadMllpAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), ct);
            if (read == 0)
                throw new IOException("connection closed before the start block arrived");
            if (buffer[0] == StartBlock)
                break;
        }

        using var messageBytes = new MemoryStream();
        var previousWasEndBlock = false;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), ct);
            if (read == 0)
                throw new IOException("connection closed mid-message");
            if (previousWasEndBlock && buffer[0] == CarriageReturn)
                break;
            if (previousWasEndBlock)
                messageBytes.WriteByte(EndBlock);
            previousWasEndBlock = buffer[0] == EndBlock;
            if (!previousWasEndBlock)
                messageBytes.WriteByte(buffer[0]);
        }

        return Encoding.UTF8.GetString(messageBytes.ToArray());
    }
}
