using System.Security.Claims;
using System.Text.Json;
using EventStore.Inbox;
using EventStore.Interchange.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Interchange;

public static class InterchangeEndpoints
{
    public static IServiceCollection AddInterchange(this IServiceCollection services) => services
        .AddKeyedScoped<IInterchangeFormatAdapter, Hl7V2Adapter>("Hl7V2")
        .AddKeyedScoped<IInterchangeFormatAdapter, FhirAdapter>("Fhir")
        .AddKeyedScoped<IInterchangeFormatAdapter, IchE2bR3Adapter>("IchE2bR3")
        .AddKeyedScoped<IInterchangeFormatAdapter, Gs1EpcisAdapter>("Gs1Epcis")
        // Always registered -- Hl7V2MllpListener checks Hl7V2MllpOptions.Enabled
        // itself, inside ExecuteAsync, the same "opt-in behavior checked post-DI"
        // shape as every other config-gated background worker in this repo.
        .AddHostedService<Hl7V2MllpListener>();

    // ADR-072 -- FHIR is RESTful/HTTP-native, needs no MLLP-style bridge:
    // an ordinary Minimal API resource consumer, unlike HL7v2's own
    // dedicated Hl7V2MllpListener (a background TCP listener, not an HTTP
    // endpoint at all).
    public static WebApplication MapInterchangeEndpoints(this WebApplication app)
    {
        app.MapPost("/interchange/fhir/{appId}", async (string appId, HttpRequest request, ClaimsPrincipal user, IServiceProvider services, PublishService publish, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var raw = await reader.ReadToEndAsync(ct);

            var adapter = services.GetRequiredKeyedService<IInterchangeFormatAdapter>("Fhir");
            InterchangeInboundResult parsed;
            try
            {
                parsed = await adapter.ParseInboundAsync(appId, raw, ct);
            }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or JsonException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var publishRequest = new PublishEventRequest(appId, 1, parsed.Payload, null, null, ReviewPending: parsed.ReviewPending);
            var result = await publish.PublishAsync(parsed.EventType, publishRequest, user, ct);
            return result switch
            {
                PublishResult.Accepted a => Results.Accepted($"/publish/{parsed.EventType}/{a.CorrelationId}", new { correlationId = a.CorrelationId, sequenceNumber = a.SequenceNumber }),
                PublishResult.UnregisteredEventType => Results.Problem(statusCode: 500, detail: $"'{parsed.EventType}' is not registered for AppId '{appId}'"),
                PublishResult.Forbidden => Results.Forbid(),
                _ => Results.Problem(statusCode: 500),
            };
        }).RequireAuthorization("events:publish");

        return app;
    }
}
