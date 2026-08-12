using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                // ADR-013 -- RFC 9457 Problem Details, per docs/adrs/adr-013-problem-details.md's own table.
                PublishResult.Conflict => Results.Problem(
                    type: "https://eventstore.example/problems/event-id-conflict",
                    title: "eventId already used with different content",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["eventId"] = request.EventId }),
                PublishResult.UnregisteredEventType => Results.Problem(
                    type: "https://eventstore.example/problems/not-found",
                    title: $"event type '{eventType}' is not registered",
                    statusCode: StatusCodes.Status404NotFound),
                PublishResult.Forbidden => Results.Forbid(),
                PublishResult.UnresolvedParent p => Results.Problem(
                    type: "https://eventstore.example/problems/parent-not-found",
                    title: "One or more parent events do not exist",
                    detail: "parentEventIds referenced an event that has not been published.",
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["missingParentEventIds"] = p.MissingParentEventIds }),
                // ADR-066/RFC 9470 -- the challenge itself is plain HTTP
                // headers (RFC 9470 §5), built here (an HTTP-response
                // concern) rather than in PublishService. `Results.Forbid()`
                // above can't carry a custom WWW-Authenticate value, so this
                // is a hand-built 401 rather than that helper.
                PublishResult.StepUpRequired su => BuildStepUpChallenge(su, httpContext),
                PublishResult.MissingSignatureMeaning => Results.Problem(
                    type: "https://eventstore.example/problems/missing-signature-meaning",
                    title: "meaning is required when the target event type has RequiredSignature configured",
                    statusCode: StatusCodes.Status400BadRequest),
                _ => Results.Problem(statusCode: 500),
            };
        }).RequireAuthorization("events:publish");

        // ADR-072 -- one HTTP round trip, N independent publishes: each item
        // still goes through PublishService.PublishAsync's own per-event
        // transaction/idempotency/hash-chain path (ADR-023), not one shared
        // batch transaction -- batching is a transport optimization, never a
        // different persistence guarantee. The OUTER response is always 202,
        // even when some items are rejected/malformed -- a batch never fails
        // or succeeds as a unit; each item's own outcome (including a
        // malformed item's own 400-shaped rejection) is reported inside the
        // response array, in submission order, never by varying the one
        // actual HTTP status code a single response can carry.
        app.MapPost("/publish/batch", async (HttpRequest httpRequest, ClaimsPrincipal user, PublishService service, CancellationToken ct) =>
        {
            using var reader = new StreamReader(httpRequest.Body);
            var body = await reader.ReadToEndAsync(ct);

            JsonArray items;
            try
            {
                items = JsonNode.Parse(body) as JsonArray ?? throw new JsonException("body must be a JSON array");
            }
            catch (JsonException)
            {
                return Results.Problem(
                    type: "https://eventstore.example/problems/malformed-batch",
                    title: "request body must be a JSON array of batch items",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var results = new List<object>();
            foreach (var itemNode in items)
            {
                BatchPublishItem? item;
                try
                {
                    item = itemNode.Deserialize<BatchPublishItem>(JsonSerializerOptions.Web);
                }
                catch (JsonException ex)
                {
                    results.Add(new { httpStatus = 400, error = "malformed batch item", detail = ex.Message });
                    continue;
                }

                // System.Text.Json silently assigns null for a missing JSON
                // property on a non-nullable reference-typed record parameter
                // (nullable annotations aren't runtime-enforced) -- checked
                // explicitly rather than trusting the type system alone.
                if (item is null || string.IsNullOrEmpty(item.EventType) || item.Request is null
                    || string.IsNullOrEmpty(item.Request.AppId) || item.Request.Payload is null)
                {
                    results.Add(new { httpStatus = 400, error = "malformed batch item -- eventType, request.appId, and request.payload are required" });
                    continue;
                }

                var result = await service.PublishAsync(item.EventType, item.Request, user, ct);
                results.Add(BuildBatchItemEnvelope(item.EventType, result));
            }

            return Results.Accepted(value: results);
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

        // ADR-013 -- the WWW-Authenticate header above carries RFC 9470's
        // own challenge parameters; the body is still an ordinary Problem
        // Details response, just with the same values also surfaced there
        // for a caller that only inspects the body.
        return Results.Problem(
            type: "https://eventstore.example/problems/insufficient-user-authentication",
            title: "insufficient_user_authentication",
            statusCode: StatusCodes.Status401Unauthorized,
            extensions: new Dictionary<string, object?> { ["acrValues"] = stepUp.AcrValues, ["maxAge"] = stepUp.MaxAge });
    }

    // The same per-item envelope /publish/{eventType} returns via distinct
    // HTTP status codes, restated here as an explicit httpStatus FIELD --
    // a batch response can only ever carry ONE real HTTP status (202,
    // always), so each item's own would-be status rides inside the body
    // instead. WWW-Authenticate's own header mechanics don't apply per-item
    // in a shared response either -- StepUpRequired's challenge parameters
    // are still fully present in the body, just not as a real header.
    private static object BuildBatchItemEnvelope(string eventType, PublishResult result) => result switch
    {
        PublishResult.Accepted a => new
        {
            eventType, httpStatus = 202, correlationId = a.CorrelationId, status = a.Status,
            entityId = string.IsNullOrEmpty(a.EntityId) ? null : a.EntityId,
            schemaStatus = a.SchemaStatus, authorityStatus = a.AuthorityStatus,
            conflictFlag = a.ConflictFlag, reason = a.Reason, sequenceNumber = a.SequenceNumber,
            originId = (string?)null, // ADR-033/090 -- same single-site-deployment null as the single-publish envelope above; found missing from this shape by a compliance audit
        },
        PublishResult.Conflict => new { eventType, httpStatus = 409, error = "eventId already used with different content" },
        PublishResult.UnregisteredEventType => new { eventType, httpStatus = 404, error = $"event type '{eventType}' is not registered" },
        PublishResult.Forbidden => new { eventType, httpStatus = 403, error = "forbidden" },
        PublishResult.UnresolvedParent p => new { eventType, httpStatus = 400, error = "parent event not found", missingParentEventIds = p.MissingParentEventIds },
        PublishResult.StepUpRequired su => new { eventType, httpStatus = 401, error = "insufficient_user_authentication", acrValues = su.AcrValues, maxAge = su.MaxAge },
        PublishResult.MissingSignatureMeaning => new { eventType, httpStatus = 400, error = "meaning is required when the target event type has RequiredSignature configured" },
        _ => new { eventType, httpStatus = 500, error = "internal error" },
    };
}

// ADR-072 -- one submission inside a /publish/batch request; EventType is
// the route-parameter equivalent /publish/{eventType} carries positionally,
// restated per-item here since a batch has no single route to carry it.
public record BatchPublishItem(string EventType, PublishEventRequest Request);
