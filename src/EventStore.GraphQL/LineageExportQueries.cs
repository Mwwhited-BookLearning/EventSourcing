using System.Security.Claims;
using EventStore.Domain.AccessLog;
using EventStore.LineageExport;
using EventStore.Persistence;
using HotChocolate;
using Microsoft.AspNetCore.Authorization;

namespace EventStore.GraphQL;

// docs/03-api-contracts.md, "Lineage export and bitemporal playback"
// (ADR-068) -- two new Query fields, both reads, enforced through the
// identical RequiredClaims/masking/access-audit pipeline as any other
// query, never a privileged bypass.
[ExtendObjectType(OperationTypeNames.Query)]
public class LineageExportQueries
{
    public async Task<LineageExportResultNode> GetExportLineageAsync(
        string entityId, [Service] ClaimsPrincipal user, LineageExportService exportService,
        LineageExportBundleStore bundleStore, IAuthorizationService authorizationService, EventStoreContext db, CancellationToken ct)
    {
        await GraphQlAuth.RequireScopeAsync(authorizationService, user, "events:lineage:read");

        var check = await exportService.CheckRootAsync(entityId, user, ct);
        if (check is LineageExportRootCheck.NotFound)
            throw new GraphQLException("Unknown entityId.");
        if (check is LineageExportRootCheck.Forbidden)
            throw new GraphQLException("Forbidden -- caller lacks the required Read claim for this entity's own root event(s).");

        var (readerActorId, readerTrustBasis, grantRef) = AccessLogReaderContext.Resolve(user);
        var bundle = await exportService.ExportAsync(entityId, user, readerActorId, ct);
        var exportId = bundleStore.Store(bundle);

        await AccessLogAppender.AppendAsync(db, readerActorId, readerTrustBasis, grantRef, "Authoritative", entityId, "export", ct);

        return new LineageExportResultNode($"/lineage-exports/{exportId}");
    }

    public async Task<PlaybackResultNode?> GetPlaybackAsOfAsync(
        string entityId, long asOfSequenceNumber, [Service] ClaimsPrincipal user, BitemporalPlaybackService playback,
        IAuthorizationService authorizationService, EventStoreContext db, CancellationToken ct)
    {
        await GraphQlAuth.RequireScopeAsync(authorizationService, user, "events:lineage:read");

        var result = await playback.ReconstructAsync(entityId, asOfSequenceNumber, user, ct);
        if (result is null)
            return null;

        var (readerActorId, readerTrustBasis, grantRef) = AccessLogReaderContext.Resolve(user);
        await AccessLogAppender.AppendAsync(db, readerActorId, readerTrustBasis, grantRef, "Authoritative", entityId, "playback", ct);

        return new PlaybackResultNode(result.AsOfSequenceNumber, result.Data, result.Extensions, result.LateArrivalCorrectionShown);
    }
}

public record LineageExportResultNode(string BundleUrl);

public record PlaybackResultNode(long AsOfSequenceNumber, string Data, string Extensions, bool LateArrivalCorrectionShown);
