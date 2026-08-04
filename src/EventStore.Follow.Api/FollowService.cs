using System.Diagnostics;
using System.Linq.Expressions;
using System.Security.Claims;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Follow.Api;

public class FollowService(EventStoreContext db, SchemaRegistryService schemaRegistry, EventTailReader tailReader)
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

        // ADR-028 -- downcast is read-time-only with no safe pass-through: every
        // hop between the requested (older) version and the active one must have
        // a registered DowncastToPrevious, checked once here at connect time
        // rather than discovered mid-stream.
        if (request.AsOfSchemaVersion is { } asOfVersion)
        {
            if (asOfVersion > definition.Version)
                return new FollowResult.ValidationFailed(
                    $"asOfSchemaVersion {asOfVersion} is newer than the active version {definition.Version}");

            if (asOfVersion < definition.Version)
            {
                var hopVersions = Enumerable.Range(asOfVersion + 1, definition.Version - asOfVersion).ToList();
                var hopDefinitions = await schemaRegistry.GetVersionsByNameAsync(normalizedName, hopVersions, ct);
                var missingHop = hopVersions.FirstOrDefault(v =>
                    !hopDefinitions.TryGetValue(v, out var hopDefinition) || string.IsNullOrEmpty(hopDefinition.DowncastToPrevious));
                if (missingHop != 0)
                    return new FollowResult.ValidationFailed(
                        $"no downcastToPrevious registered for version {missingHop} -- cannot serve asOfSchemaVersion {asOfVersion}");
            }
        }

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

        var events = tailReader.TailAsync(normalizedName, predicate, lastSeen, request.AsOfSchemaVersion, PollInterval, user, ct);
        return new FollowResult.Connected(events);
    }
}
