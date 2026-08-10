using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Inbox;

public static class PublishEndpoints
{
    public static IServiceCollection AddInbox(this IServiceCollection services) => services
        .AddScoped<PublishService>()
        .AddScoped<ChainVerificationService>()
        .AddScoped<AccessLogChainVerificationService>();

    public static WebApplication MapPublishEndpoints(this WebApplication app)
    {
        app.MapPost("/publish/{eventType}", async (string eventType, PublishEventRequest request, ClaimsPrincipal user, PublishService service, HttpContext httpContext, CancellationToken ct) =>
        {
            var result = await service.PublishAsync(eventType, request, user, ct);
            return result switch
            {
                // ADR-023 -- the persist-everything envelope; entityId is ""
                // rather than the JSON-friendlier null until the Router
                // resolves it, matching StoredEvent.EntityId's own sentinel.
                PublishResult.Accepted a => Results.Accepted($"/publish/{eventType}/{a.CorrelationId}", new
                {
                    correlationId = a.CorrelationId,
                    status = a.Status,
                    entityId = string.IsNullOrEmpty(a.EntityId) ? null : a.EntityId,
                    schemaStatus = a.SchemaStatus,
                    authorityStatus = a.AuthorityStatus,
                    conflictFlag = a.ConflictFlag,
                    reason = a.Reason,
                    sequenceNumber = a.SequenceNumber,
                    originId = (string?)null, // ADR-033/090 -- null for this single-site deployment
                }),
                PublishResult.Conflict => Results.Conflict(new { error = "eventId already used with different content" }),
                PublishResult.UnregisteredEventType => Results.NotFound(new { error = $"event type '{eventType}' is not registered" }),
                PublishResult.Forbidden => Results.Forbid(),
                PublishResult.UnresolvedParent p => Results.BadRequest(new { error = "parent event not found", missingParentEventIds = p.MissingParentEventIds }),
                // ADR-066/RFC 9470 -- the challenge itself is plain HTTP
                // headers (RFC 9470 §5), built here (an HTTP-response
                // concern) rather than in PublishService. `Results.Forbid()`
                // above can't carry a custom WWW-Authenticate value, so this
                // is a hand-built 401 rather than that helper.
                PublishResult.StepUpRequired su => BuildStepUpChallenge(su, httpContext),
                PublishResult.MissingSignatureMeaning => Results.BadRequest(new { error = "meaning is required when the target event type has RequiredSignature configured" }),
                _ => Results.Problem(statusCode: 500),
            };
        }).RequireAuthorization("events:publish");

        // ADR-019 -- an operator/audit capability, not a Publish/Follow one;
        // reuses registry:admin, the existing "admin tier" scope, rather than
        // inventing a new one for this single endpoint.
        app.MapGet("/events/verify", async (long throughSequenceNumber, ChainVerificationService service, CancellationToken ct) =>
        {
            var result = await service.VerifyAsync(throughSequenceNumber, ct);
            return result switch
            {
                ChainVerificationResult.Verified v => Results.Ok(new { verified = true, eventCount = v.EventCount }),
                ChainVerificationResult.Tampered t => Results.Ok(new { verified = false, firstDivergentSequenceNumber = t.FirstDivergentSequenceNumber }),
                _ => Results.Problem(statusCode: 500),
            };
        }).RequireAuthorization("registry:admin");

        // ADR-045 -- the AccessLog analogue of /events/verify above, same
        // operator/audit scope, its own independent chain/service.
        app.MapGet("/access-log/verify", async (long throughSequenceNumber, AccessLogChainVerificationService service, CancellationToken ct) =>
        {
            var result = await service.VerifyAsync(throughSequenceNumber, ct);
            return result switch
            {
                ChainVerificationResult.Verified v => Results.Ok(new { verified = true, eventCount = v.EventCount }),
                ChainVerificationResult.Tampered t => Results.Ok(new { verified = false, firstDivergentSequenceNumber = t.FirstDivergentSequenceNumber }),
                _ => Results.Problem(statusCode: 500),
            };
        }).RequireAuthorization("registry:admin");

        return app;
    }

    // RFC 9470 §5's own challenge format, verified against the actual RFC
    // text before writing this, not recalled from memory: `Bearer
    // error="insufficient_user_authentication"[, acr_values="..."][,
    // max_age="..."]`, 401 -- acr_values is space-separated when more than
    // one value is configured; either parameter is omitted entirely when
    // RequiredSignature didn't configure it (never emitted as an empty
    // string).
    private static IResult BuildStepUpChallenge(PublishResult.StepUpRequired stepUp, HttpContext httpContext)
    {
        var parameters = new List<string> { "error=\"insufficient_user_authentication\"" };
        if (stepUp.AcrValues.Count > 0)
            parameters.Add($"acr_values=\"{string.Join(' ', stepUp.AcrValues)}\"");
        if (stepUp.MaxAge is { } maxAge)
            parameters.Add($"max_age=\"{maxAge}\"");
        httpContext.Response.Headers["WWW-Authenticate"] = $"Bearer {string.Join(", ", parameters)}";

        return Results.Json(
            new { error = "insufficient_user_authentication", acrValues = stepUp.AcrValues, maxAge = stepUp.MaxAge },
            statusCode: StatusCodes.Status401Unauthorized);
    }
}
