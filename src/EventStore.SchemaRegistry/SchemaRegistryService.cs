using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;

namespace EventStore.SchemaRegistry;

public class SchemaRegistryService(
    EventStoreContext db, IFilterableFieldIndexDdlGenerator indexDdlGenerator, IMemoryCache cache, IUpcastExpressionEvaluator upcastEvaluator,
    ISchemaChangeNotifier? schemaChangeNotifier = null)
{
    // Must equal EventStore.SpecGeneration.OpenApiDocumentBuilder.CacheKey /
    // AsyncApiDocumentBuilder.CacheKey -- duplicated rather than referenced,
    // since SchemaRegistry has no reason to depend on SpecGeneration just for
    // two constants (docs/06-solution-structure.md: "SchemaRegistryService
    // calls IMemoryCache.Remove(...) on both keys after a successful
    // registration").
    private const string OpenApiDocumentCacheKey = "openapi-document";
    private const string AsyncApiDocumentCacheKey = "asyncapi-document";

    public async Task<RegisterEventTypeResult> RegisterAsync(
        string eventTypeName, RegisterEventTypeRequest request, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var normalizedName = eventTypeName.ToLowerInvariant();

        var schemaNode = TryParseJsonSchema(request.JsonSchema, errors);

        if (!Enum.TryParse<ChangeKind>(request.ChangeKind, ignoreCase: true, out var changeKind))
            errors.Add($"changeKind is required and must be \"Full\" or \"Partial\" (got: {request.ChangeKind ?? "<missing>"})");

        var parentValidationMode = ParentValidationMode.Strict;
        if (request.ParentValidationMode is { } pvmText &&
            !Enum.TryParse(pvmText, ignoreCase: true, out parentValidationMode))
            errors.Add($"parentValidationMode must be \"Strict\" or \"Permissive\" (got: {pvmText})");

        var rejectionBehavior = RejectionBehavior.Annotate;
        if (request.RejectionBehavior is { } rbText &&
            !Enum.TryParse(rbText, ignoreCase: true, out rejectionBehavior))
            errors.Add($"rejectionBehavior must be \"Annotate\" or \"Compensate\" (got: {rbText})");

        var requiredClaims = new List<RequiredClaim>();
        foreach (var claimRequest in request.RequiredClaims ?? [])
        {
            if (!Enum.TryParse<ClaimDirection>(claimRequest.Direction, ignoreCase: true, out var direction))
            {
                errors.Add($"requiredClaims[].direction must be \"Publish\" or \"Read\" (got: {claimRequest.Direction})");
                continue;
            }
            if (!IsTypeValueFormat(claimRequest.Claim))
            {
                errors.Add($"requiredClaims[].claim must be in \"type:value\" format (got: {claimRequest.Claim})");
                continue;
            }
            requiredClaims.Add(new RequiredClaim { Direction = direction, Claim = claimRequest.Claim });
        }

        var filterableFields = new List<FilterableField>();
        foreach (var fieldRequest in request.FilterableFields)
        {
            if (!JsonPathValidation.IsSafe(fieldRequest.JsonPath))
            {
                errors.Add($"filterableFields[].jsonPath must be a simple dotted-identifier chain (got: {fieldRequest.JsonPath})");
                continue;
            }
            if (schemaNode is not null && !ResolvesInSchema(schemaNode, fieldRequest.JsonPath))
            {
                errors.Add($"filterableFields[].jsonPath does not resolve in jsonSchema: {fieldRequest.JsonPath}");
                continue;
            }
            if (!Enum.TryParse<FilterableFieldType>(fieldRequest.DataType, ignoreCase: true, out var dataType))
            {
                errors.Add($"filterableFields[].dataType must be one of String, Number, Boolean, DateTimeOffset (got: {fieldRequest.DataType})");
                continue;
            }
            filterableFields.Add(new FilterableField
            {
                EventTypeAppId = request.AppId,
                EventTypeName = normalizedName,
                JsonPath = fieldRequest.JsonPath,
                DataType = dataType,
                IsIndexed = fieldRequest.IsIndexed,
            });
        }

        if (schemaNode is JsonObject schemaObject)
        {
            MaskingSchemaValidator.Validate(schemaObject, errors);
            EnumFallbackSchemaValidator.Validate(schemaObject, errors);
        }

        // ADR-018 -- an alias that doesn't name an actual property of the
        // destination (this registration's own) schema, or an expression that
        // fails to parse, is rejected 400 at registration time. This narrows,
        // but does not close, registration-time compatibility checking --
        // whether the expression's *output* actually validates against the
        // destination schema is ADR-020's job (publish-time), not this one's.
        if (!string.IsNullOrEmpty(request.UpcastFromPrevious))
        {
            if (!UpcastExpressionListParser.TryParse(request.UpcastFromPrevious, out var upcastClauses, out var parseError))
            {
                errors.Add($"upcastFromPrevious: {parseError}");
            }
            else
            {
                foreach (var clause in upcastClauses)
                {
                    if (!upcastEvaluator.TryCompile(clause.Expression, out var compileError))
                        errors.Add($"upcastFromPrevious: expression '{clause.Expression}' failed to parse: {compileError}");
                    if (schemaNode is not JsonObject destSchema || destSchema["properties"] is not JsonObject destProperties ||
                        !destProperties.ContainsKey(clause.Alias))
                        errors.Add($"upcastFromPrevious: alias '{clause.Alias}' does not name a property of this version's own schema");
                }
            }
        }

        if (errors.Count > 0)
            return new RegisterEventTypeResult.ValidationFailed(errors);

        var priorActiveVersion = await db.EventTypeDefinitions
            .Where(e => e.AppId == request.AppId && e.Name == normalizedName && e.IsActive)
            .SingleOrDefaultAsync(ct);

        var newVersion = (priorActiveVersion?.Version ?? 0) + 1;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (priorActiveVersion is not null)
        {
            priorActiveVersion.IsActive = false;
            db.EventTypeDefinitions.Update(priorActiveVersion);
        }

        var definition = new EventTypeDefinition
        {
            AppId = request.AppId,
            Name = normalizedName,
            Version = newVersion,
            JsonSchema = request.JsonSchema,
            RegisteredAt = DateTimeOffset.UtcNow,
            IsActive = true,
            ParentValidationMode = parentValidationMode,
            RequiredClaims = requiredClaims,
            ChangeKind = changeKind,
            EntityIdField = request.EntityIdField ?? "",
            EntityType = string.IsNullOrEmpty(request.EntityType) ? normalizedName : request.EntityType.ToLowerInvariant(),
            UpcastFromPrevious = request.UpcastFromPrevious,
            DowncastToPrevious = request.DowncastToPrevious,
            RejectionBehavior = rejectionBehavior,
            FilterableFields = filterableFields,
        };
        db.EventTypeDefinitions.Add(definition);

        await db.SaveChangesAsync(ct);

        foreach (var field in filterableFields.Where(f => f.IsIndexed))
        {
            var indexName = $"IX_Events_{request.AppId}_{normalizedName}_{newVersion}_{field.Id}";
            var ddlStatements = indexDdlGenerator.GenerateCreateIndexDdl("Events", "Payload", field.JsonPath, indexName);
            // Not db.Database.ExecuteSqlRawAsync -- found while implementing that it always
            // parses the SQL as a composite format string (RawSqlCommandBuilder.Build), even
            // with no parameters supplied. PostgreSQL's own path-array literal syntax ('{Amount}')
            // uses literal curly braces, which that parser misreads as a {0}-style placeholder.
            // A raw ADO.NET command against the same connection/transaction has no such parsing.
            var connection = db.Database.GetDbConnection();
            var dbTransaction = transaction.GetDbTransaction();
            foreach (var statement in ddlStatements)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement;
                command.Transaction = dbTransaction;
                await command.ExecuteNonQueryAsync(ct);
            }
        }

        await transaction.CommitAsync(ct);

        cache.Remove(OpenApiDocumentCacheKey); // ADR-002 -- ~60s TTL otherwise; invalidate immediately on registration
        cache.Remove(AsyncApiDocumentCacheKey);
        schemaChangeNotifier?.NotifyChanged(); // "GraphQL-Only Query Layer" -- same immediate-invalidation discipline, for the dynamically-built Subscription schema

        return new RegisterEventTypeResult.Success(newVersion);
    }

    public async Task<EventTypeDefinition?> GetActiveAsync(string appId, string eventTypeName, CancellationToken ct = default) =>
        await db.EventTypeDefinitions
            .Include(e => e.FilterableFields)
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.AppId == appId && e.Name == eventTypeName.ToLowerInvariant() && e.IsActive, ct);

    public async Task<EventTypeDefinition?> GetVersionAsync(string appId, string eventTypeName, int version, CancellationToken ct = default) =>
        await db.EventTypeDefinitions
            .Include(e => e.FilterableFields)
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.AppId == appId && e.Name == eventTypeName.ToLowerInvariant() && e.Version == version, ct);

    // Read-time RequiredClaims lookup with no AppId available -- Follow/Lineage
    // callers only ever have a bare EventType name/StoredEvent, not the AppId a
    // Publish request always supplies. Resolves by (Name, IsActive) alone. Per
    // docs/10-open-questions.md row 1, this is a documented, deliberate
    // simplification: ADR-030 allows two different AppIds to register the same
    // type name independently, and nothing yet disambiguates which one's
    // RequiredClaims governs a bare stored event (EntityId's embedded AppId
    // prefix, ADR-021, isn't populated until "Entity-Centric Core Rebuild").
    // Deterministic-but-arbitrary tie-break on a genuine collision: ordered by
    // AppId, first wins. No matching active definition at all defaults to
    // "unrestricted" rather than failing closed, so a data inconsistency can't
    // accidentally lock out an otherwise-valid stored event.
    public async Task<IReadOnlyList<RequiredClaim>> GetActiveClaimsByNameAsync(string eventTypeName, CancellationToken ct = default)
    {
        var definition = await db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => e.Name == eventTypeName.ToLowerInvariant() && e.IsActive)
            .OrderBy(e => e.AppId)
            .FirstOrDefaultAsync(ct);
        return definition?.RequiredClaims ?? [];
    }

    // Batch form of GetActiveClaimsByNameAsync, for Lineage's ancestor/descendant
    // traversal -- one query for every distinct EventType name discovered in the
    // reachable set, instead of one round trip per node.
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RequiredClaim>>> GetActiveClaimsByNamesAsync(
        IReadOnlyCollection<string> eventTypeNames, CancellationToken ct = default)
    {
        var normalizedNames = eventTypeNames.Select(n => n.ToLowerInvariant()).Distinct().ToList();
        var definitions = await db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => normalizedNames.Contains(e.Name) && e.IsActive)
            .OrderBy(e => e.AppId)
            .ToListAsync(ct);
        return definitions
            .GroupBy(e => e.Name)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RequiredClaim>)g.First().RequiredClaims);
    }

    // "Hardening & Evolution" -- UpcastChain needs the event type's own
    // CURRENT active version as the upcast destination, given only a bare
    // EventType name (Follow has no AppId per event, same docs/10-open-
    // questions.md row 1 gap every other bare-name lookup here shares).
    public async Task<EventTypeDefinition?> GetActiveDefinitionByNameAsync(string eventTypeName, CancellationToken ct = default) =>
        await db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => e.Name == eventTypeName.ToLowerInvariant() && e.IsActive)
            .OrderBy(e => e.AppId)
            .FirstOrDefaultAsync(ct);

    // "CQRS Read-Model Projections" -- ProjectionHost needs each followed
    // event type's ChangeKind (ADR-016) to know Full-replace vs. Partial-merge,
    // but has no direct service/DB reference at all (docs/06-solution-
    // structure.md: "its only dependency on the write side is an HTTP client"),
    // so this must be reachable over HTTP -- see SchemaRegistryEndpoints'
    // new GET /registry/{eventType}/change-kind. Same bare-name,
    // tie-break-by-AppId simplification as GetActiveClaimsByNameAsync
    // (docs/10-open-questions.md row 1).
    public async Task<ChangeKind?> GetActiveChangeKindByNameAsync(string eventTypeName, CancellationToken ct = default)
    {
        var definition = await db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => e.Name == eventTypeName.ToLowerInvariant() && e.IsActive)
            .OrderBy(e => e.AppId)
            .FirstOrDefaultAsync(ct);
        return definition?.ChangeKind;
    }

    // Batch, bare-name-and-version lookup for Follow's per-event masking
    // (docs/10-open-questions.md row 1's same AppId-ambiguity simplification as
    // GetActiveClaimsByNamesAsync above: resolve by (Name, Version) alone,
    // deterministic-but-arbitrary tie-break ordered by AppId on a genuine
    // collision). Masking must use each event's own SchemaVersion, not
    // whichever version is currently active -- a payload's shape always
    // matches the version it was originally validated against.
    public async Task<IReadOnlyDictionary<int, EventTypeDefinition>> GetVersionsByNameAsync(
        string eventTypeName, IReadOnlyCollection<int> versions, CancellationToken ct = default)
    {
        var normalizedName = eventTypeName.ToLowerInvariant();
        var definitions = await db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => e.Name == normalizedName && versions.Contains(e.Version))
            .OrderBy(e => e.AppId)
            .ToListAsync(ct);
        return definitions
            .GroupBy(e => e.Version)
            .ToDictionary(g => g.Key, g => g.First());
    }

    // AppId-scoped counterpart to GetVersionsByNameAsync -- Publish's own
    // context always has an explicit AppId (the request's own field), so it
    // never needs that method's bare-name tie-break workaround
    // (docs/10-open-questions.md row 1).
    public async Task<IReadOnlyDictionary<int, EventTypeDefinition>> GetVersionsAsync(
        string appId, string eventTypeName, IReadOnlyCollection<int> versions, CancellationToken ct = default)
    {
        var normalizedName = eventTypeName.ToLowerInvariant();
        var definitions = await db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => e.AppId == appId && e.Name == normalizedName && versions.Contains(e.Version))
            .ToListAsync(ct);
        return definitions.ToDictionary(e => e.Version, e => e);
    }

    // Temporary listing surface for this build stage -- plain HTTP QUERY with
    // $top/$skip (ADR-012), superseded by the GraphQL eventTypes(...) resolver
    // once "GraphQL-Only Query Layer" lands (see the correction note on this
    // item in docs/08-build-plan.md).
    public async Task<IReadOnlyList<EventTypeDefinition>> ListAsync(string appId, int? top, int? skip, CancellationToken ct = default)
    {
        var query = db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => e.AppId == appId)
            .OrderBy(e => e.Name).ThenBy(e => e.Version);

        IQueryable<EventTypeDefinition> paged = query;
        if (skip is { } s) paged = paged.Skip(s);
        if (top is { } t) paged = paged.Take(t);

        return await paged.ToListAsync(ct);
    }

    // Hand-written, not JsonSchema.Net -- tried and reverted while implementing this
    // item (see docs/changes/2026-08-03.md): that library's default parse rejects any
    // undeclared/vendor keyword ("Unknown keywords are disallowed for this dialect")
    // unless the document declares $schema/$vocabulary or the caller registers a
    // custom Dialect -- incompatible with this design's pervasive, undeclared
    // "x-masking" extension. This check only catches genuine structural mistakes
    // (an invalid "type" value, a non-object "properties"/mis-shaped "items") and
    // tolerates any unrecognized keyword, which is exactly the behavior this design
    // actually needs.
    private static readonly string[] ValidSchemaTypes =
        ["object", "array", "string", "number", "integer", "boolean", "null"];

    private static JsonNode? TryParseJsonSchema(string jsonSchemaText, List<string> errors)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(jsonSchemaText);
        }
        catch (JsonException)
        {
            errors.Add("jsonSchema is not valid JSON");
            return null;
        }

        if (!IsWellFormedSchemaNode(node))
            errors.Add("jsonSchema is not a well-formed JSON Schema document");

        return node;
    }

    private static bool IsWellFormedSchemaNode(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<bool>(out _))
            return true; // a bare `true`/`false` is a valid JSON Schema

        if (node is not JsonObject obj)
            return false;

        if (obj["type"] is { } typeNode)
        {
            var typeValues = typeNode is JsonArray typeArray
                ? typeArray.Select(t => t?.GetValue<string>())
                : [typeNode.GetValue<string>()];
            if (typeValues.Any(t => t is null || !ValidSchemaTypes.Contains(t)))
                return false;
        }

        if (obj["properties"] is { } propertiesNode)
        {
            if (propertiesNode is not JsonObject properties)
                return false;
            if (properties.Any(p => !IsWellFormedSchemaNode(p.Value)))
                return false;
        }

        return obj["items"] is not { } itemsNode || IsWellFormedSchemaNode(itemsNode);
    }

    private static bool ResolvesInSchema(JsonNode schemaNode, string jsonPath)
    {
        var current = schemaNode;
        foreach (var segment in JsonPathValidation.Segments(jsonPath))
        {
            if (current is not JsonObject obj || obj["properties"] is not JsonObject properties ||
                !properties.TryGetPropertyValue(segment, out var next) || next is null)
                return false;
            current = next;
        }
        return true;
    }

    private static bool IsTypeValueFormat(string claim)
    {
        var parts = claim.Split(':', 2);
        return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0;
    }
}
