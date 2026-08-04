using System.Security.Claims;
using EventStore.TicketExchange;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Attachments;

public static class AttachmentEndpoints
{
    public static IServiceCollection AddAttachments(this IServiceCollection services) => services
        .AddScoped<AttachmentService>()
        .AddSingleton<IAttachmentContentStore, InMemoryAttachmentContentStore>();

    public static WebApplication MapAttachmentEndpoints(this WebApplication app)
    {
        // ADR-032 -- the two-step handoff's first step: raw bytes in, a
        // ContentHash out. MimeType/FileName/RequiredReadClaim/
        // RequiredPublishClaim travel as headers/query, not a JSON body --
        // the body itself is the raw content, never wrapped in an envelope.
        app.MapPost("/attachments", async (
            HttpContext context, AttachmentService service, string? fileName, string? requiredReadClaim, string? requiredPublishClaim) =>
        {
            using var memoryStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(memoryStream, context.RequestAborted);
            var mimeType = context.Request.ContentType ?? "application/octet-stream";

            var result = await service.UploadAsync(
                memoryStream.ToArray(), mimeType, fileName, requiredReadClaim, requiredPublishClaim,
                context.User, context.RequestAborted);

            return result switch
            {
                UploadAttachmentResult.Created created => Results.Created($"/attachments/{created.ContentHash}", new { contentHash = created.ContentHash }),
                UploadAttachmentResult.Deduplicated deduplicated => Results.Ok(new { contentHash = deduplicated.ContentHash }),
                UploadAttachmentResult.Forbidden => Results.Forbid(),
                _ => Results.Problem(statusCode: 500),
            };
        }).RequireAuthorization("attachments:ingest");

        // ADR-031's Range-request support, reused unchanged (RFC 7233) --
        // the same seekable-retrieval reasoning that applies to a large
        // PDF/image as it does to Media channel playback. ADR-040's own
        // second named header-incapable target (an <img src>/<a href>) --
        // gated by BOTH schemes, same reasoning as StreamingEndpoints'
        // playback route.
        app.MapGet("/attachments/{contentHash}", async (
            string contentHash, AttachmentService service, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var result = await service.RetrieveAsync(contentHash, user, ct);
            return result switch
            {
                RetrieveAttachmentResult.Found found => Results.Bytes(found.Bytes, found.MimeType, found.FileName, enableRangeProcessing: true),
                RetrieveAttachmentResult.NotFound => Results.NotFound(),
                RetrieveAttachmentResult.Forbidden => Results.Forbid(),
                _ => Results.Problem(statusCode: 500),
            };
        }).RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{TicketAuthenticationDefaults.AuthenticationScheme}",
            Policy = "attachments:read",
        });

        return app;
    }
}
