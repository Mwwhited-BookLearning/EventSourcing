using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using EventStore.Inbox;
using EventStore.Interchange.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStore.Interchange;

// ADR-072 -- HL7v2's real transport is MLLP over TCP, not HTTP (nearly
// every production hospital interface uses it, matching Google Cloud's
// own published MLLP adapter, the real, concrete precedent this class
// follows). MLLP framing itself: 0x0B (start block) + message bytes +
// 0x1C 0x0D (end block + carriage return) -- verified against the actual
// MLLP specification before writing this, not approximated. MLLP has NO
// transport security of its own; this listener adds none either -- TLS
// termination or network-level isolation is a real, named, un-mitigated
// deployment responsibility (this ADR's own text), not a gap this class
// silently introduces.
public class Hl7V2MllpListener(IServiceScopeFactory scopeFactory, IOptions<Hl7V2MllpOptions> options, ILogger<Hl7V2MllpListener> logger) : BackgroundService
{
    private const byte StartBlock = 0x0B;
    private const byte EndBlock = 0x1C;
    private const byte CarriageReturn = 0x0D;

    // ADR-035/042 -- MLLP-sourced messages have no bearer token of their
    // own (TCP, not HTTP) -- an empty principal, same "no claims-bypass
    // concern" posture ChannelLagDetectedEventType's own publish already
    // established. Authorization for this path is TLS/network isolation
    // (this ADR's own named deployment responsibility), never a claim.
    private static readonly ClaimsPrincipal SystemPrincipal = new(new ClaimsIdentity());

    // Exposed for tests: Hl7V2MllpOptions.Port = 0 lets the OS assign a
    // free port (avoiding a fixed test port colliding under parallel test
    // execution) -- this is how the test then learns which one it got. A
    // real deployment always configures an explicit port instead.
    public int? BoundPort { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;

        var listener = new TcpListener(IPAddress.Any, options.Value.Port);
        listener.Start();
        BoundPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        logger.LogInformation("HL7v2 MLLP listener started on port {Port}", BoundPort);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            try
            {
                while (client.Connected && !ct.IsCancellationRequested)
                {
                    var message = await ReadMllpMessageAsync(stream, ct);
                    if (message is null)
                        break; // the sender closed the connection

                    var ack = await ProcessMessageAsync(message, ct);
                    await WriteMllpMessageAsync(stream, ack, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "HL7v2 MLLP connection ended abnormally");
            }
        }
    }

    private async Task<string> ProcessMessageAsync(string rawMessage, CancellationToken ct)
    {
        var controlId = ExtractMessageControlId(rawMessage);
        using var scope = scopeFactory.CreateScope();
        try
        {
            // Resolved by the same "Hl7V2" key AddInterchange registers,
            // not hardcoded to the concrete Hl7V2Adapter type -- a
            // deployment could register a customized adapter under this
            // same key and this listener would use it unchanged.
            var adapter = scope.ServiceProvider.GetRequiredKeyedService<IInterchangeFormatAdapter>("Hl7V2");
            var parsed = await adapter.ParseInboundAsync(options.Value.AppId, rawMessage, ct);
            var publish = scope.ServiceProvider.GetRequiredService<PublishService>();
            var result = await publish.PublishAsync(
                parsed.EventType,
                new PublishEventRequest(options.Value.AppId, 1, parsed.Payload, null, null, ReviewPending: parsed.ReviewPending),
                SystemPrincipal, ct);

            return result is PublishResult.Accepted
                ? BuildAck(controlId, accepted: true, null)
                : BuildAck(controlId, accepted: false, $"publish rejected: {result.GetType().Name}");
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
            logger.LogWarning(ex, "HL7v2 message rejected");
            return BuildAck(controlId, accepted: false, ex.Message);
        }
    }

    // MSH-10 (message control ID) is field index 9 in a NON-MSH segment's
    // own numbering, but MSH's own field 1 IS the separator character
    // itself -- the same offset-by-one quirk Hl7V2Adapter's own MSH-9
    // parsing already accounts for.
    private static string ExtractMessageControlId(string rawMessage)
    {
        var msh = rawMessage.Split('\r', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(s => s.StartsWith("MSH|", StringComparison.Ordinal));
        var fields = msh?.Split('|') ?? [];
        return fields.Length > 9 ? fields[9] : "UNKNOWN";
    }

    private static string BuildAck(string controlId, bool accepted, string? errorText)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var ackControlId = Guid.NewGuid().ToString("N")[..10];
        var msh = $"MSH|^~\\&|EventStore|EventStore|||{timestamp}||ACK|{ackControlId}|P|2.3";
        var msa = accepted ? $"MSA|AA|{controlId}" : $"MSA|AE|{controlId}|{errorText}";
        return $"{msh}\r{msa}\r";
    }

    private static async Task<string?> ReadMllpMessageAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[1];
        // Skip bytes until the start block -- some senders emit stray
        // bytes/keepalives between messages on a kept-open connection.
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), ct);
            if (read == 0)
                return null; // connection closed by the sender
            if (buffer[0] == StartBlock)
                break;
        }

        using var messageBytes = new MemoryStream();
        var previousWasEndBlock = false;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), ct);
            if (read == 0)
                return null; // connection closed mid-message

            if (previousWasEndBlock && buffer[0] == CarriageReturn)
                break; // EndBlock + CarriageReturn -- the real MLLP trailer

            if (previousWasEndBlock)
                messageBytes.WriteByte(EndBlock); // a lone 0x1C that wasn't actually the trailer -- part of the message

            previousWasEndBlock = buffer[0] == EndBlock;
            if (!previousWasEndBlock)
                messageBytes.WriteByte(buffer[0]);
        }

        return Encoding.UTF8.GetString(messageBytes.ToArray());
    }

    private static async Task WriteMllpMessageAsync(NetworkStream stream, string message, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        await stream.WriteAsync(new[] { StartBlock }, ct);
        await stream.WriteAsync(payload, ct);
        await stream.WriteAsync(new[] { EndBlock, CarriageReturn }, ct);
        await stream.FlushAsync(ct);
    }
}
