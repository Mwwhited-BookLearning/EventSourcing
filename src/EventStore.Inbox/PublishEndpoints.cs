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
        .AddScoped<ChainVerificationService>();

    public static WebApplication MapPublishEndpoints(this WebApplication app)
    {
        app.MapPost("/publish/{eventType}", async (string eventType, PublishEventRequest request, ClaimsPrincipal user, PublishService service, CancellationToken ct) =>
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

        return app;
    }
}
