using System.Security.Claims;
using System.Text.Json;
using EventStore.Domain.AccessLog;
using EventStore.Persistence;
using EventStore.TicketExchange;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Streaming;

public static class StreamingEndpoints
{
    public static IServiceCollection AddStreaming(this IServiceCollection services) => services
        .AddScoped<ChannelRegistryService>()
        .AddScoped<TelemetrySampleWriter>()
        .AddScoped<TelemetryTailReader>()
        .AddScoped<MediaFragmentResolver>()
        .AddSingleton<StreamRedactionResolver>()
        .AddKeyedSingleton<IStreamRedactionStrategy, ZeroFillStrategy>("ZeroFill")
        .AddKeyedSingleton<IStreamRedactionStrategy, ToneStrategy>("Tone")
        .AddKeyedSingleton<IStreamRedactionStrategy, BlankFrameStrategy>("BlankFrame")
        .AddKeyedSingleton<IStreamRedactionStrategy, PartialRevealStreamRedactionStrategy>("PartialReveal")
        .AddHostedService<ChannelDerivationWorker>();

    public static WebApplication MapStreamingEndpoints(this WebApplication app)
    {
        app.MapPut("/telemetry/channels/{channelId}", async (string channelId, RegisterChannelRequest request, ChannelRegistryService service, CancellationToken ct) =>
        {
            var result = await service.RegisterAsync(channelId, request, ct);
            return result switch
            {
                RegisterChannelResult.Success => Results.Created($"/telemetry/channels/{channelId}", new { channelId }),
                RegisterChannelResult.ValidationFailed failed => Results.BadRequest(new { errors = failed.Errors }),
                _ => Results.Problem(statusCode: 500),
            };
        }).RequireAuthorization("telemetry:ingest");

        app.MapPost("/telemetry/{channelId}/samples", async (string channelId, IngestSamplesRequest request, TelemetrySampleWriter writer, CancellationToken ct) =>
        {
            var result = await writer.IngestAsync(channelId, request, ct);
            return result switch
            {
                IngestSamplesResult.Accepted accepted => Results.Accepted(value: new
                {
                    channelId = accepted.ChannelId,
                    samplesWritten = accepted.SamplesWritten,
                    lateArrivalCount = accepted.LateArrivalCount,
                }),
                IngestSamplesResult.ChannelNotFound => Results.NotFound(),
                IngestSamplesResult.ValidationFailed failed => Results.BadRequest(new { error = failed.Error }),
                _ => Results.Problem(statusCode: 500),
            };
        }).RequireAuthorization("telemetry:ingest");

        // Dual-mode by design, per HTTP's own conditional-request semantics:
        // a Range header switches this same resource to raw byte-range
        // retrieval over the channel's concatenated chunk bytes (ADR-031's
        // playback/seeking need -- RFC 7233); its absence serves the live
        // JSON/SSE tail/replay stream instead (ADR-010's shape, reused).
        // The byte-range mode is ADR-040's own named "Streaming Channel
        // playback" header-incapable target (a <video src>) -- gated by
        // BOTH schemes (Bearer, the default, and Ticket, ADR-040), so an
        // ordinarily-authenticated caller is completely unaffected and a
        // ticket+sig caller with no Authorization header at all also
        // succeeds.
        app.MapGet("/telemetry/{channelId}/samples", async (
            string channelId, string? mode, DateTimeOffset? fromTimestamp,
            EventStoreContext db, TelemetryTailReader reader, ClaimsPrincipal user, HttpContext context) =>
        {
            if (context.Request.Headers.ContainsKey("Range"))
                return await ServeByteRangeAsync(channelId, db, user, context.RequestAborted);

            await ServeTailAsync(await reader.ConnectAsync(channelId, mode, fromTimestamp, user, context.RequestAborted), context);
            return Results.Empty;
        }).RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{TicketAuthenticationDefaults.AuthenticationScheme}",
            Policy = "telemetry:read",
        });

        app.MapGet("/telemetry/sessions/{threadId}/samples", async (
            string threadId, string? mode, DateTimeOffset? fromTimestamp,
            TelemetryTailReader reader, ClaimsPrincipal user, HttpContext context) =>
        {
            await ServeTailAsync(await reader.ConnectByThreadIdAsync(threadId, mode, fromTimestamp, user, context.RequestAborted), context);
            return Results.Empty;
        }).RequireAuthorization("telemetry:read");

        return app;
    }

    private static async Task<IResult> ServeByteRangeAsync(string channelId, EventStoreContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var channel = await db.TelemetryChannels.AsNoTracking().SingleOrDefaultAsync(c => c.ChannelId == channelId, ct);
        if (channel is null)
            return Results.NotFound();

        // SQLite's EF provider can't translate ORDER BY over DateTimeOffset --
        // the same limitation TelemetryTailReader/ChannelDerivationWorker's
        // own queries already work around; ordered client-side instead.
        var samples = await db.TelemetrySamples.AsNoTracking()
            .Where(s => s.ChannelId == channelId)
            .ToListAsync(ct);
        var bytes = samples.OrderBy(s => s.Timestamp).SelectMany(s => s.Value).ToArray();

        // ADR-045 -- ADR-040's own named "Streaming Channel playback" target
        // (a <video src>/<audio src>); the live SSE tail mode above is a
        // per-sample subscription, not this named single-request read, so
        // it's deliberately not also logged here, one entry per sample.
        var (readerActorId, readerTrustBasis, grantRef) = AccessLogReaderContext.Resolve(user);
        await AccessLogAppender.AppendAsync(db, readerActorId, readerTrustBasis, grantRef, "Authoritative", channelId, "stream", ct);

        return Results.Bytes(bytes, channel.MimeType ?? "application/octet-stream", enableRangeProcessing: true);
    }

    private static async Task ServeTailAsync(TelemetryTailResult result, HttpContext context)
    {
        switch (result)
        {
            case TelemetryTailResult.ChannelNotFound:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                break;

            case TelemetryTailResult.Forbidden:
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                break;

            case TelemetryTailResult.ValidationFailed failed:
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = failed.Error });
                break;

            case TelemetryTailResult.Connected connected:
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.Headers.ContentType = "text/event-stream";
                context.Response.Headers.CacheControl = "no-cache";
                await context.Response.Body.FlushAsync(context.RequestAborted);

                await foreach (var sample in connected.Samples.WithCancellation(context.RequestAborted))
                {
                    var envelope = JsonSerializer.Serialize(new
                    {
                        channelId = sample.ChannelId,
                        timestamp = sample.Timestamp,
                        value = sample.Value,
                        lateArrivalFlag = sample.LateArrivalFlag,
                        redactionAppliedFlag = sample.RedactionAppliedFlag,
                    });
                    await context.Response.WriteAsync($"data: {envelope}\n\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
                break;
        }
    }
}
