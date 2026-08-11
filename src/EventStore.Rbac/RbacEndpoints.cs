using System.Security.Claims;
using EventStore.Inbox;
using EventStore.SchemaRegistry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EventStore.Rbac;

// ADR-067 -- the Host-side write path for RBAC role/permission grants and
// AppTrustRoot registration: real Bearer+scope-gated Minimal API endpoints
// (the same registry:admin/registry:trust-admin tier SchemaRegistryEndpoints
// already uses, per that item's own precedent), publishing a reserved event
// through the ordinary PublishService rather than a bespoke CRUD write.
// EventStore.DevIdp's own former /oauth/roles /oauth/role-assignments
// /oauth/user-permissions /oauth/trust-roots endpoints are retired in favor
// of these -- DevIdp now only ever FOLLOWS these events (RbacProjectionWorker)
// to keep its own local read models current, per direct request (checked
// explicitly before building, not assumed): the write path moves here so it
// gets real, auditable, scope-gated auth; DevIdp keeps a fast local read at
// token-issuance time by folding this event stream into its own tables
// rather than calling back into a Host synchronously on every token request.
public static class RbacEndpoints
{
    public static WebApplication MapRbacEndpoints(this WebApplication app)
    {
        var roles = app.MapGroup("/rbac/roles").RequireAuthorization("registry:admin");

        roles.MapPost("/{roleName}/assignments", async (string roleName, GrantRoleRequest request, ClaimsPrincipal user, SchemaRegistryService schemaRegistry, PublishService publish, CancellationToken ct) =>
        {
            if (!AppIdScopeEvaluator.CanAdminister(user, request.AppId))
                return Results.Forbid();

            await RoleGrantedEventType.EnsureRegisteredAsync(schemaRegistry, request.AppId, ct);
            var payload = $$"""{ "ActorId": {{System.Text.Json.JsonSerializer.Serialize(request.ActorId)}}, "RoleName": {{System.Text.Json.JsonSerializer.Serialize(roleName)}}, "AssignmentKey": {{System.Text.Json.JsonSerializer.Serialize($"{request.ActorId}:{roleName}")}} }""";
            var result = await publish.PublishAsync(RoleGrantedEventType.Name, new PublishEventRequest(request.AppId, 1, payload, null, null), user, ct);
            return ToResult(result);
        });

        // DELETE requests don't support an inferred body parameter in
        // Minimal APIs (only POST/PUT/PATCH do) -- the same constraint
        // EventStore.DevIdp's own retired /oauth/role-assignments DELETE
        // endpoint already documented; query parameters instead.
        roles.MapDelete("/{roleName}/assignments", async (string roleName, string appId, string actorId, ClaimsPrincipal user, SchemaRegistryService schemaRegistry, PublishService publish, CancellationToken ct) =>
        {
            if (!AppIdScopeEvaluator.CanAdminister(user, appId))
                return Results.Forbid();

            await RoleRevokedEventType.EnsureRegisteredAsync(schemaRegistry, appId, ct);
            var payload = $$"""{ "ActorId": {{System.Text.Json.JsonSerializer.Serialize(actorId)}}, "RoleName": {{System.Text.Json.JsonSerializer.Serialize(roleName)}}, "AssignmentKey": {{System.Text.Json.JsonSerializer.Serialize($"{actorId}:{roleName}")}} }""";
            var result = await publish.PublishAsync(RoleRevokedEventType.Name, new PublishEventRequest(appId, 1, payload, null, null), user, ct);
            return ToResult(result);
        });

        app.MapPost("/rbac/permissions", async (GrantPermissionRequest request, ClaimsPrincipal user, SchemaRegistryService schemaRegistry, PublishService publish, CancellationToken ct) =>
        {
            if (!AppIdScopeEvaluator.CanAdminister(user, request.AppId))
                return Results.Forbid();

            await PermissionGrantedEventType.EnsureRegisteredAsync(schemaRegistry, request.AppId, ct);
            var payload = $$"""{ "ActorId": {{System.Text.Json.JsonSerializer.Serialize(request.ActorId)}}, "Permission": {{System.Text.Json.JsonSerializer.Serialize(request.Permission)}}, "GrantKey": {{System.Text.Json.JsonSerializer.Serialize($"{request.ActorId}:{request.Permission}")}} }""";
            var result = await publish.PublishAsync(PermissionGrantedEventType.Name, new PublishEventRequest(request.AppId, 1, payload, null, null), user, ct);
            return ToResult(result);
        }).RequireAuthorization("registry:admin");

        // ADR-044 -- a separate scope from registry:admin, deliberately
        // (neither implies the other, per that ADR's own decision, unchanged
        // by this item). AppIdScopeEvaluator.CanAdminister is deliberately
        // NOT applied here -- it only recognizes "registry:admin"-shaped
        // scopes, and ADR-044 has never modeled an AppId-scoped
        // "registry:trust-admin:{appId}" variant (only the flat, unscoped
        // form is seeded/registered anywhere) -- found only by running this
        // (a real registry:trust-admin-scoped caller was wrongly 403'd).
        // The coarse RequireAuthorization("registry:trust-admin") group gate
        // below is this endpoint's only AppId-agnostic-by-design check.
        app.MapPut("/rbac/trust-roots/{issuerDid}", async (string issuerDid, RegisterTrustRootRequest request, ClaimsPrincipal user, SchemaRegistryService schemaRegistry, PublishService publish, CancellationToken ct) =>
        {
            await AppTrustRootRegisteredEventType.EnsureRegisteredAsync(schemaRegistry, request.AppId, ct);
            // Description is omitted entirely when absent, not serialized as
            // an explicit JSON null -- the registered schema declares it as
            // plain "type": "string" (see AppTrustRootRegisteredEventType's
            // own comment), which a literal null value would fail.
            var payload = request.Description is { } description
                ? $$"""{ "IssuerDid": {{System.Text.Json.JsonSerializer.Serialize(issuerDid)}}, "Description": {{System.Text.Json.JsonSerializer.Serialize(description)}} }"""
                : $$"""{ "IssuerDid": {{System.Text.Json.JsonSerializer.Serialize(issuerDid)}} }""";
            var result = await publish.PublishAsync(AppTrustRootRegisteredEventType.Name, new PublishEventRequest(request.AppId, 1, payload, null, null), user, ct);
            return ToResult(result);
        }).RequireAuthorization("registry:trust-admin");

        return app;
    }

    private static IResult ToResult(PublishResult result) => result switch
    {
        PublishResult.Accepted a => Results.Created($"/events/{a.CorrelationId}", new { correlationId = a.CorrelationId, sequenceNumber = a.SequenceNumber }),
        PublishResult.Conflict => Results.Conflict(),
        PublishResult.UnregisteredEventType => Results.Problem(statusCode: 500, detail: "the reserved event type was not registered before publishing -- this is an EnsureRegisteredAsync bug, not a caller error"),
        PublishResult.Forbidden => Results.Forbid(),
        PublishResult.UnresolvedParent p => Results.BadRequest(new { error = "parent event not found", missingParentEventIds = p.MissingParentEventIds }),
        _ => Results.Problem(statusCode: 500),
    };
}

public record GrantRoleRequest(string AppId, string ActorId);
public record GrantPermissionRequest(string AppId, string ActorId, string Permission);
public record RegisterTrustRootRequest(string AppId, string? Description);
