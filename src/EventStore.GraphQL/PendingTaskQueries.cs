using System.Security.Claims;
using EventStore.Domain.SchemaRegistry;
using EventStore.Flows;
using HotChocolate;
using Microsoft.EntityFrameworkCore;

namespace EventStore.GraphQL;

// ADR-101 -- read-only surface over EventStore.Flows' PendingTasksDbContext.
// No ORDER BY on purpose: the caller's own instruction was "the task list
// can be out of order so it should just be a query," and the ProjectionHost
// that fills this table never guarantees any particular arrival order across
// domains anyway. RequiredClaim filtering happens in memory -- it isn't a
// SQL-translatable predicate (RequiredClaimEvaluator.HasClaim reuses the
// same "type:value" primitive as schema registry claim checks) and this
// table is expected to stay small (open tasks only).
[ExtendObjectType(OperationTypeNames.Query)]
public class PendingTaskQueries
{
    [GraphQLName("myTasks")]
    public async Task<IReadOnlyList<PendingTask>> GetMyTasksAsync(
        [Service] ClaimsPrincipal user, PendingTasksDbContext db, CancellationToken ct)
    {
        var allTasks = await db.PendingTasks.AsNoTracking().ToListAsync(ct);
        return allTasks
            .Where(t => t.RequiredClaim is null || RequiredClaimEvaluator.HasClaim(user, t.RequiredClaim))
            .ToList();
    }
}
