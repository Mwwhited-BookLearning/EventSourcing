using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EventStore.Webhooks;

// docs/features/webhooks.md's own illustrative "webhooks:admin" scope
// (03-api-contracts.md never enumerated a webhook-management endpoint at
// all -- this doc's own reasonable extrapolation, not a cited ADR fact).
// Rotation/discard-previous-secret (ADR-093) are deliberately NOT mapped
// here -- "Signing Secret Rotation, Dual Signature" is its own later
// build-plan item.
public static class WebhookEndpoints
{
    public static WebApplication MapWebhookEndpoints(this WebApplication app)
    {
        app.MapPost("/webhooks/subscriptions", async (RegisterWebhookSubscriptionRequest request, ClaimsPrincipal user, WebhookSubscriptionService subscriptions, CancellationToken ct) =>
        {
            var subscription = await subscriptions.RegisterAsync(request.AppId, request.TargetUrl, request.EventTypes, request.SigningSecret, user, request.OutboundAdapterKey, ct);
            return Results.Created($"/webhooks/subscriptions/{subscription.SubscriptionId}", new
            {
                subscriptionId = subscription.SubscriptionId,
                signingSecret = subscription.SigningSecret, // shown once at creation, same as most real webhook providers -- never re-displayed later
            });
        }).RequireAuthorization("webhooks:admin");

        return app;
    }
}

public record RegisterWebhookSubscriptionRequest(string AppId, string TargetUrl, List<string> EventTypes, string? SigningSecret, string? OutboundAdapterKey = null);
