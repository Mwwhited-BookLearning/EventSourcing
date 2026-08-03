using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Follow.Api;

public static class FollowEndpoints
{
    public static IServiceCollection AddFollowApi(this IServiceCollection services) => services
        .AddScoped<EventTailReader>()
        .AddScoped<FollowService>();

    public static WebApplication MapFollowEndpoints(this WebApplication app)
    {
        app.MapMethods("/follow/{eventType}", ["QUERY"], async (string eventType, FollowRequest request, FollowService service, HttpContext context) =>
        {
            var result = await service.ConnectAsync(eventType, request, context.RequestAborted);
            switch (result)
            {
                case FollowResult.UnregisteredEventType:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    break;

                case FollowResult.ValidationFailed failed:
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new { error = failed.Error });
                    break;

                case FollowResult.Connected connected:
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.Headers.ContentType = "text/event-stream";
                    context.Response.Headers.CacheControl = "no-cache";
                    await context.Response.Body.FlushAsync(context.RequestAborted); // open the stream immediately

                    await foreach (var storedEvent in connected.Events.WithCancellation(context.RequestAborted))
                    {
                        var envelope = JsonSerializer.Serialize(new
                        {
                            eventId = storedEvent.EventId,
                            sequenceNumber = storedEvent.SequenceNumber,
                            occurredAt = storedEvent.OccurredAt,
                            parentEventIds = Array.Empty<Guid>(), // populated once "Event-Type Security"'s restricted-parent omission lands
                            payload = JsonNode.Parse(storedEvent.Payload),
                        });
                        await context.Response.WriteAsync($"data: {envelope}\n\n", context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                    }
                    break;
            }
        });

        return app;
    }
}
