using System.Text.Json.Nodes;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Derivation;

public class DerivationRegistrationService(EventStoreContext db, SchemaRegistryService schemaRegistry, IUpcastExpressionEvaluator expressionEvaluator)
{
    public async Task<RegisterDerivationResult> RegisterAsync(string eventTypeName, RegisterDerivationRequest request, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var normalizedName = eventTypeName.ToLowerInvariant();
        var normalizedSources = request.From.Select(s => s.ToLowerInvariant()).ToList();

        if (normalizedSources.Count == 0)
            errors.Add("$from must name at least one source event type");

        if (!Enum.TryParse<JoinTriggerMode>(request.JoinTriggerMode, ignoreCase: true, out var joinTriggerMode))
            errors.Add($"joinTriggerMode must be \"FireOnce\" or \"ContinuousEnrichment\" (got: {request.JoinTriggerMode})");

        if (!Enum.TryParse<BackfillMode>(request.BackfillMode, ignoreCase: true, out var backfillMode))
            errors.Add($"backfillMode must be \"FromHistory\" or \"FromNow\" (got: {request.BackfillMode})");

        var joinConditions = new List<JoinCondition>();
        if (normalizedSources.Count > 0 && !OnClauseParser.TryParse(request.On, normalizedSources, out joinConditions, out var onError))
            errors.Add(onError!);

        var selectFields = new List<SelectField>();
        if (normalizedSources.Count > 0 && !SelectClauseParser.TryParse(request.Select, normalizedSources, expressionEvaluator, out selectFields, out var selectError))
            errors.Add(selectError!);

        if (joinTriggerMode == JoinTriggerMode.FireOnce && request.PendingJoinTtlSeconds is not > 0)
            errors.Add("pendingJoinTtlSeconds is required and must be positive when joinTriggerMode is FireOnce");

        if (errors.Count > 0)
            return new RegisterDerivationResult.ValidationFailed(errors);

        if (await WouldCreateCycleAsync(request.AppId, normalizedName, normalizedSources, ct))
            return new RegisterDerivationResult.ValidationFailed(
                [$"registering {eventTypeName} with these $from sources would create a derivation-definition cycle"]);

        // Resolves every source's currently-active schema -- confirms each named
        // source is actually registered, and supplies the field-level schemas
        // ComposeSchema below copies from, rather than declaring every derived
        // field as an untyped catch-all.
        var sourceSchemas = new Dictionary<string, JsonNode>();
        foreach (var source in normalizedSources.Distinct())
        {
            var definition = await schemaRegistry.GetActiveAsync(request.AppId, source, ct);
            if (definition is null)
            {
                errors.Add($"$from source is not a registered event type: {source}");
                continue;
            }
            sourceSchemas[source] = JsonNode.Parse(definition.JsonSchema)!;
        }

        if (errors.Count > 0)
            return new RegisterDerivationResult.ValidationFailed(errors);

        var composedSchema = ComposeSchema(selectFields, sourceSchemas);

        // Two sequential saves, not one transaction: SchemaRegistryService.RegisterAsync
        // already owns its own internal transaction and commits it before returning,
        // so composing a second BeginTransactionAsync around it here would nest
        // transactions on the same connection, which most ADO.NET providers reject.
        // A crash between the two leaves a registered EventTypeDefinition with no
        // matching DerivationDefinition -- harmless (nothing publishes to it, it just
        // looks like an ordinary unused registration) rather than a correctness gap,
        // so this narrower, honestly-documented seam is preferred over refactoring
        // SchemaRegistryService to accept an externally-supplied transaction.
        var registerResult = await schemaRegistry.RegisterAsync(normalizedName, new RegisterEventTypeRequest(
            AppId: request.AppId,
            JsonSchema: composedSchema.ToJsonString(),
            FilterableFields: [],
            ChangeKind: "Full",
            EntityIdField: null,
            ParentValidationMode: "Permissive",
            RequiredClaims: null,
            UpcastFromPrevious: null,
            DowncastToPrevious: null), ct);

        if (registerResult is not RegisterEventTypeResult.Success)
        {
            return new RegisterDerivationResult.ValidationFailed(
                registerResult is RegisterEventTypeResult.ValidationFailed failed
                    ? failed.Errors
                    : ["derived event type registration failed"]);
        }

        var currentMaxSequenceNumber = await db.Events.Select(e => (long?)e.SequenceNumber).MaxAsync(ct) ?? 0L;
        var derivedSourceNames = (await db.DerivationDefinitions
            .AsNoTracking()
            .Where(d => d.AppId == request.AppId && normalizedSources.Distinct().Contains(d.Name))
            .Select(d => d.Name)
            .ToListAsync(ct)).ToHashSet();

        db.DerivationDefinitions.Add(new DerivationDefinition
        {
            AppId = request.AppId,
            Name = normalizedName,
            Sources = normalizedSources,
            JoinConditions = joinConditions,
            SelectFields = selectFields,
            JoinTriggerMode = joinTriggerMode,
            BackfillMode = backfillMode,
            BackfillThroughDerivedSources = request.BackfillThroughDerivedSources,
            PendingJoinTtl = TimeSpan.FromSeconds(request.PendingJoinTtlSeconds ?? 0),
            MaxHopCount = request.MaxHopCount ?? 5,
            RegisteredAt = DateTimeOffset.UtcNow,
            IsActive = true,
        });

        foreach (var source in normalizedSources.Distinct())
        {
            // BackfillThroughDerivedSources (ADR-007) only matters when the overall
            // mode is FromHistory: a source that is itself a derived type either gets
            // its own full history replayed too (true, the default backfill-everything
            // posture) or is treated as starting fresh from registration time (false) --
            // FromNow overall already starts every source from now regardless.
            var sourceStartsFromHistory = backfillMode == BackfillMode.FromHistory &&
                (request.BackfillThroughDerivedSources || !derivedSourceNames.Contains(source));

            db.DerivationCursors.Add(new DerivationCursor
            {
                AppId = request.AppId,
                DerivationName = normalizedName,
                SourceEventType = source,
                LastProcessedSequenceNumber = sourceStartsFromHistory ? 0L : currentMaxSequenceNumber,
            });
        }

        await db.SaveChangesAsync(ct);

        return new RegisterDerivationResult.Success();
    }

    // ADR-007: a plain DFS with a visited-set over the small, admin-scale
    // graph of derivation *definitions themselves* (derived type -> its
    // declared $from sources) -- distinct from ADR-005's CycleGuard, which
    // guards a single traversal of the already-published, inert event DAG.
    private async Task<bool> WouldCreateCycleAsync(string appId, string newName, List<string> newSources, CancellationToken ct)
    {
        var existingDefinitions = await db.DerivationDefinitions
            .AsNoTracking()
            .Where(d => d.AppId == appId)
            .Select(d => new { d.Name, d.Sources })
            .ToListAsync(ct);
        var sourcesByName = existingDefinitions.ToDictionary(d => d.Name, d => d.Sources);

        var visited = new HashSet<string>();
        var stack = new Stack<string>(newSources);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == newName)
                return true;
            if (!visited.Add(current))
                continue;
            if (sourcesByName.TryGetValue(current, out var upstreamSources))
                foreach (var upstreamSource in upstreamSources)
                    stack.Push(upstreamSource);
        }

        return false;
    }

    private static JsonObject ComposeSchema(List<SelectField> selectFields, IReadOnlyDictionary<string, JsonNode> sourceSchemas)
    {
        var properties = new JsonObject();
        foreach (var field in selectFields)
        {
            // Fallback for a calculated field (no single source field to copy a
            // type from -- ADR-007 addendum) and for a straight mapping whose
            // source field can't be resolved in its schema.
            JsonNode fieldSchema = new JsonObject { ["type"] = "string" };

            if (field.SourceType is not null &&
                sourceSchemas.TryGetValue(field.SourceType, out var schemaNode) &&
                schemaNode is JsonObject schemaObject &&
                schemaObject["properties"] is JsonObject sourceProperties &&
                sourceProperties.TryGetPropertyValue(field.SourceField!, out var sourceFieldSchema) &&
                sourceFieldSchema is not null)
            {
                fieldSchema = sourceFieldSchema.DeepClone();
            }

            properties[field.OutputField] = fieldSchema;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(selectFields.Select(f => (JsonNode)f.OutputField).ToArray()),
        };
    }
}
