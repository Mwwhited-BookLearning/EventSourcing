using System.Security.Claims;
using EventStore.Lineage.Api;
using EventStore.Persistence;
using HotChocolate;
using Microsoft.AspNetCore.Authorization;

namespace EventStore.GraphQL;

// docs/03-api-contracts.md, "Lineage API -- GraphQL query fields (ADR-037)":
// `event(eventId) { ancestors descendants parents children }`, reusing
// LineageService unchanged -- only the transport (a GraphQL field instead of
// four separate QUERY /events/{id}/... paths) changed. `first` replaces the
// pre-ADR-037 $top; this item's own doc text names $top/$skip's replacement
// as "first/after cursor-style arguments... rather than a bespoke offset/
// limit pair," but the doc's own shown example (`ancestors(first: 50) { ... }`
// as a flat list, not `edges { node { ... } } }`) never actually uses `after`
// -- HotChocolate's [UsePaging] Connection-wrapping wasn't adopted here since
// it would produce a shape the doc itself never shows; `first`/`skip` are
// plain arguments applied inside LineageService, same as its pre-existing
// top/skip parameters. Honestly narrower than a full Relay cursor
// implementation, flagged in 08-build-plan.md.
[ExtendObjectType(OperationTypeNames.Query)]
public class LineageQueries
{
    [GraphQLName("event")]
    public async Task<LineageEventNode> GetEventAsync(Guid eventId, [Service] ClaimsPrincipal user, LineageService lineage, IAuthorizationService authorizationService, CancellationToken ct)
    {
        await GraphQlAuth.RequireScopeAsync(authorizationService, user, "events:lineage:read");

        var check = await lineage.CheckRootAsync(eventId, user, ct);
        return check switch
        {
            LineageRootCheck.NotFound => throw new GraphQLException("Unknown eventId."),
            LineageRootCheck.Forbidden => throw new GraphQLException("Forbidden -- caller lacks the required Read claim for this event's type."),
            _ => new LineageEventNode(eventId),
        };
    }
}

// A thin resolver-carrying node -- ancestors/descendants/parents/children are
// each their own field so HotChocolate only calls LineageService for the
// specific traversal a query document actually selects, never all four.
public class LineageEventNode(Guid eventId)
{
    public Guid EventId { get; } = eventId;

    public Task<IReadOnlyList<LineageNode>> GetAncestorsAsync(int? first, int? skip, [Service] ClaimsPrincipal user, LineageService lineage, CancellationToken ct) =>
        lineage.GetAncestorsAsync(EventId, user, first, skip, ct);

    public Task<IReadOnlyList<LineageNode>> GetDescendantsAsync(int? first, int? skip, [Service] ClaimsPrincipal user, LineageService lineage, CancellationToken ct) =>
        lineage.GetDescendantsAsync(EventId, user, first, skip, ct);

    public Task<IReadOnlyList<LineageNode>> GetParentsAsync(int? first, int? skip, [Service] ClaimsPrincipal user, LineageService lineage, CancellationToken ct) =>
        lineage.GetParentsAsync(EventId, user, first, skip, ct);

    public Task<IReadOnlyList<LineageNode>> GetChildrenAsync(int? first, int? skip, [Service] ClaimsPrincipal user, LineageService lineage, CancellationToken ct) =>
        lineage.GetChildrenAsync(EventId, user, first, skip, ct);
}
