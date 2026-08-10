using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace EventStore.Gateway;

// ADR-058 -- for /publish and /follow, the target AppId lives in the JSON
// request body (PublishEventRequest.AppId, FollowRequest's own appId
// field), not a claim. Rate limiting must decide a partition key BEFORE
// YARP forwards the request, but the body is a single-read forward-only
// stream that YARP itself still needs to send on to the Host unchanged --
// EnableBuffering() lets this middleware read it once, then rewind
// Request.Body back to position 0 so both the rate limiter (via
// TenantPartitionKey, reading HttpContext.Items) and YARP's own proxied
// forward see the exact same, untouched body. A body that isn't valid
// JSON (or has no "appId" property) just means this AppId's own bucket
// can't be resolved -- TenantPartitionKey falls back to its own next tier
// rather than this middleware ever rejecting a request.
public class AppIdBufferingMiddleware(RequestDelegate next)
{
    public const string AppIdItemKey = "EventStore.Gateway.AppId";

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldPeekBody(context.Request))
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;

            if (TryExtractAppId(body) is { } appId)
                context.Items[AppIdItemKey] = appId;
        }

        await next(context);
    }

    private static bool ShouldPeekBody(HttpRequest request) =>
        request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true &&
        (request.Path.StartsWithSegments("/publish") || request.Path.StartsWithSegments("/follow"));

    private static string? TryExtractAppId(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("appId", out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
