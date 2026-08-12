using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventStore.Derivation;

public static class DerivationEndpoints
{
    public static IServiceCollection AddDerivation(this IServiceCollection services) => services
        .AddScoped<DerivationRegistrationService>()
        .AddHostedService<DerivationWorker>();

    public static WebApplication MapDerivationEndpoints(this WebApplication app)
    {
        // ADR-007 -- reuses registry:admin (ADR-006), same tier as ordinary schema
        // registration: defining an event type is a single administrative
        // capability whether the type is hand-authored or derived.
        app.MapPost("/create/{eventType}", async (string eventType, RegisterDerivationRequest request, DerivationRegistrationService service, CancellationToken ct) =>
        {
            var result = await service.RegisterAsync(eventType, request, ct);
            return result switch
            {
                RegisterDerivationResult.Success => Results.Created($"/create/{eventType}", new { }),
                RegisterDerivationResult.ValidationFailed failed => Results.Problem(
                    type: "https://eventstore.example/problems/derivation-validation-failed",
                    title: "derivation definition failed validation",
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["errors"] = failed.Errors }),
                _ => Results.Problem(statusCode: 500),
            };
        }).RequireAuthorization("registry:admin");

        return app;
    }
}
