using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EventStore.Webhooks;

// docs/features/webhooks.md's own illustrative "webhooks:admin" scope
// (03-api-contracts.md never enumerated a webhook-management endpoint at
// all -- this doc's own reasonable extrapolation, not a cited ADR fact).
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

        // ADR-093 -- opens the rotation overlap window. Returns the new
        // secret once, same "shown once, never re-displayed" posture as
        // registration above.
        app.MapPost("/webhooks/subscriptions/{id:guid}/rotate-secret", async (Guid id, WebhookSubscriptionService subscriptions, CancellationToken ct) =>
        {
            if (await subscriptions.GetAsync(id, ct) is null)
                return Results.NotFound();
            var newSecret = await subscriptions.RotateSigningSecretAsync(id, ct);
            return Results.Ok(new { signingSecret = newSecret });
        }).RequireAuthorization("webhooks:admin");

        // ADR-093 -- ends the rotation overlap window; an explicit ops
        // action, not a framework timer (rotation cadence stays
        // ops-configurable per that ADR's own Decision text).
        app.MapPost("/webhooks/subscriptions/{id:guid}/discard-previous-secret", async (Guid id, WebhookSubscriptionService subscriptions, CancellationToken ct) =>
        {
            if (await subscriptions.GetAsync(id, ct) is null)
                return Results.NotFound();
            await subscriptions.DiscardPreviousSigningSecretAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization("webhooks:admin");

        return app;
    }
}

public record RegisterWebhookSubscriptionRequest(string AppId, string TargetUrl, List<string> EventTypes, string? SigningSecret, string? OutboundAdapterKey = null);
