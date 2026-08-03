using System.Diagnostics;
using System.Linq.Expressions;
using System.Security.Claims;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Follow.Api;

public class FollowService(EventStoreContext db, EventTailReader tailReader)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public async Task<FollowResult> ConnectAsync(string eventTypeName, FollowRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var normalizedName = eventTypeName.ToLowerInvariant();

        var definition = await db.EventTypeDefinitions
            .Include(e => e.FilterableFields)
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.AppId == request.AppId && e.Name == normalizedName && e.IsActive, ct);
        if (definition is null)
            return new FollowResult.UnregisteredEventType();

        // ADR-008/050 -- checked once, at connect time, per that ADR's own text.
        if (!RequiredClaimEvaluator.HasAny(definition.RequiredClaims, ClaimDirection.Read, user))
            return new FollowResult.Forbidden();

        var mode = FollowMode.Tail;
        if (request.Mode is { } modeText && !Enum.TryParse(modeText, ignoreCase: true, out mode))
            return new FollowResult.ValidationFailed($"mode must be \"Tail\" or \"Replay\" (got: {modeText})");

        // ADR-010 -- fromSequenceNumber is only meaningful alongside mode: Replay.
        if (request.FromSequenceNumber is not null && mode != FollowMode.Replay)
            return new FollowResult.ValidationFailed("fromSequenceNumber is only valid alongside mode: Replay");

        Expression<Func<Domain.EventLog.StoredEvent, bool>> predicate;
        try
        {
            predicate = FilterPredicateBuilder.Build(normalizedName, definition.FilterableFields, request.Filter);
        }
        catch (FilterPushdownException ex)
        {
            return new FollowResult.ValidationFailed(ex.Message);
        }

        // docs/06-solution-structure.md, "Follow: tail vs replay cursor" -- only this
        // initial value differs between the two modes; the poll loop is identical.
        long lastSeen = mode switch
        {
            FollowMode.Tail => await db.Events.AsNoTracking().MaxAsync(e => (long?)e.SequenceNumber, ct) ?? 0,
            FollowMode.Replay => request.FromSequenceNumber ?? 0,
            _ => throw new UnreachableException(),
        };

        var events = tailReader.TailAsync(normalizedName, predicate, lastSeen, PollInterval, user, ct);
        return new FollowResult.Connected(events);
    }
}
