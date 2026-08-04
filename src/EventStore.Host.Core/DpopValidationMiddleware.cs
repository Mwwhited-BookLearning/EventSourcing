using System.Security.Cryptography;
using System.Text;
using EventStore.Dpop;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace EventStore.Host.Core;

// ADR-017 -- the resource-server half of RFC 9449. Runs after
// UseAuthentication() (so JwtBearer has already validated the bearer
// token's signature/claims and populated HttpContext.User) and before
// UseAuthorization() (so a DPoP failure short-circuits before any scope
// policy would otherwise let the request through). A request with no
// bearer token at all is left alone here -- User.Identity.IsAuthenticated
// stays false, and the existing JwtBearer challenge/authorization pipeline
// already produces its own 401, unrelated to DPoP.
public static class DpopValidationMiddlewareExtensions
{
    public static WebApplication UseDpopValidation(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                await next(context);
                return;
            }

            var authorizationHeader = context.Request.Headers.Authorization.ToString();
            const string bearerPrefix = "Bearer ";
            var accessToken = authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? authorizationHeader[bearerPrefix.Length..]
                : null;

            var reason = await ValidateAsync(context, accessToken);
            if (reason is not null)
            {
                await Results.Problem(detail: reason, statusCode: StatusCodes.Status401Unauthorized, type: "dpop-proof-invalid",
                    extensions: new Dictionary<string, object?> { ["reason"] = reason }).ExecuteAsync(context);
                return;
            }

            await next(context);
        });
        return app;
    }

    private static async Task<string?> ValidateAsync(HttpContext context, string? accessToken)
    {
        if (accessToken is null)
            return "missing Authorization: Bearer token";

        var jkt = context.User.FindFirst("cnf.jkt")?.Value;
        if (jkt is null)
            return "access token is not DPoP-bound (missing cnf.jkt)";

        var expectedHtu = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}";
        var expectedAth = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));
        var proofHeader = context.Request.Headers["DPoP"].ToString();
        var replayCache = context.RequestServices.GetRequiredService<IDpopReplayCache>();

        var result = await DpopProofValidator.ValidateAsync(proofHeader, context.Request.Method, expectedHtu, expectedAth, replayCache);
        if (!result.IsValid)
            return result.Error;

        return result.Jkt == jkt ? null : "DPoP proof key does not match the access token's cnf.jkt";
    }
}
