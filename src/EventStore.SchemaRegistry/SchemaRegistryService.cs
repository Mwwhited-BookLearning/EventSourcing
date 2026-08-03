using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;

namespace EventStore.SchemaRegistry;

public class SchemaRegistryService(EventStoreContext db, IFilterableFieldIndexDdlGenerator indexDdlGenerator, IMemoryCache cache)
{
    // Must equal EventStore.SpecGeneration.OpenApiDocumentBuilder.CacheKey --
    // duplicated rather than referenced, since SchemaRegistry has no reason to
    // depend on SpecGeneration just for one constant (docs/06-solution-
    // structure.md: "SchemaRegistryService calls IMemoryCache.Remove(...) on
    // both keys after a successful registration" -- only one key exists so far).
    private const string OpenApiDocumentCacheKey = "openapi-document";

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
            MaskingSchemaValidator.Validate(schemaObject, errors);

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
            UpcastFromPrevious = request.UpcastFromPrevious,
            DowncastToPrevious = request.DowncastToPrevious,
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
