using System.Security.Claims;
using System.Text.Json;
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
        app.MapMethods("/follow/{eventType}", ["QUERY"], async (string eventType, FollowRequest request, ClaimsPrincipal user, FollowService service, HttpContext context) =>
        {
            var result = await service.ConnectAsync(eventType, request, user, context.RequestAborted);
            switch (result)
            {
                case FollowResult.UnregisteredEventType:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    break;

                case FollowResult.Forbidden:
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
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

                    await foreach (var followedEvent in connected.Events.WithCancellation(context.RequestAborted))
                    {
                        var storedEvent = followedEvent.Event;
                        var envelope = JsonSerializer.Serialize(new
                        {
                            eventId = storedEvent.EventId,
                            sequenceNumber = storedEvent.SequenceNumber,
                            occurredAt = storedEvent.OccurredAt,
                            parentEventIds = followedEvent.VisibleParentEventIds, // ADR-008 -- a restricted parent's ID is omitted here
                            payload = followedEvent.MaskedPayload, // ADR-009 -- any x-masking-annotated field is now a {value:...}/{masked:...} wrapper
                        });
                        await context.Response.WriteAsync($"data: {envelope}\n\n", context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                    }
                    break;
            }
        }).RequireAuthorization("events:follow");

        return app;
    }
}
