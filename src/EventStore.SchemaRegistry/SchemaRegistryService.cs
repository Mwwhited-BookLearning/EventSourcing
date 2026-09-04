using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Domain.AccessLog;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EventStore.SchemaRegistry;

public class SchemaRegistryService(
    EventStoreContext db, IFilterableFieldIndexDdlGenerator indexDdlGenerator, IMemoryCache cache, IUpcastExpressionEvaluator upcastEvaluator,
    ISchemaChangeNotifier? schemaChangeNotifier = null, IOptions<SchemaRegistryOriginIdOptions>? originIdOptions = null)
{
    // ADR-033 -- which site's own SchemaRegistered notification this is,
    // real and per-site-configurable (previously always the literal string
    // "local" regardless of which site actually registered it, a genuine
    // bug found while building cross-peer schema-registry replication --
    // made every peer's own notification indistinguishable from every
    // other's once synced together). RouterWorker compares an incoming
    // event's own OriginId against this to decide "mine, already applied
    // directly" vs "elsewhere, needs folding" -- see SiteOriginId below.
    private readonly string _originId = originIdOptions?.Value.OriginId ?? SchemaRegistryOriginIdOptions.Default;

    public string SiteOriginId => _originId;

    // Must equal EventStore.SpecGeneration.OpenApiDocumentBuilder.CacheKey /
    // AsyncApiDocumentBuilder.CacheKey -- duplicated rather than referenced,
    // since SchemaRegistry has no reason to depend on SpecGeneration just for
    // two constants (docs/06-solution-structure.md: "SchemaRegistryService
    // calls IMemoryCache.Remove(...) on both keys after a successful
    // registration").
    private const string OpenApiDocumentCacheKey = "openapi-document";
    private const string AsyncApiDocumentCacheKey = "asyncapi-document";

    // ADR-067 -- user is optional (trailing, defaulting to null) so every
    // pre-existing call site (dozens, across nearly every test file) is
    // completely unaffected; a real Host endpoint always passes the actual
    // caller's ClaimsPrincipal, matching ADR-064's ActorId requirement for
    // the reserved SchemaRegistered audit event this method now appends.
    public async Task<RegisterEventTypeResult> RegisterAsync(
        string eventTypeName, RegisterEventTypeRequest request, CancellationToken ct = default, ClaimsPrincipal? user = null)
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

            // ADR-096/097 -- a x-masking-searchable-with-no-matching-
            // FilterableField is a schema-authoring mistake with no error
            // raised here (MaskingSchemaValidator validates x-masking-
            // searchable's own shape independently of whether a
            // FilterableField names the same path) -- it simply can never
            // be indexed, since nothing could ever query it either.
            var (indexKind, searchableConfig) = schemaNode is not null
                ? ResolveSearchableIndexKind(schemaNode, fieldRequest.JsonPath)
                : (FilterableFieldIndexKind.PlaintextExpression, null);

            filterableFields.Add(new FilterableField
            {
                EventTypeAppId = request.AppId,
                EventTypeName = normalizedName,
                JsonPath = fieldRequest.JsonPath,
                DataType = dataType,
                IsIndexed = fieldRequest.IsIndexed,
                IndexKind = indexKind,
                SearchableConfig = searchableConfig,
            });
        }

        if (schemaNode is JsonObject schemaObject)
        {
            MaskingSchemaValidator.Validate(schemaObject, errors);
            EnumFallbackSchemaValidator.Validate(schemaObject, errors);
        }

        // ADR-066 -- naming neither AcrValues nor MaxAge is a RequiredSignature
        // that enforces nothing at all, always genuinely a request mistake
        // (the schema author meant to require SOME step-up), not a
        // legitimate "no-op" configuration -- rejected the same way a
        // present-but-empty x-masking config is elsewhere in this file.
        RequiredSignature? requiredSignature = null;
        if (request.RequiredSignature is { } requiredSignatureRequest)
        {
            if (requiredSignatureRequest.AcrValues.Count == 0 && requiredSignatureRequest.MaxAge is null)
                errors.Add("requiredSignature must set at least one of acrValues or maxAge");
            else if (requiredSignatureRequest.MaxAge is { } maxAge && maxAge <= 0)
                errors.Add($"requiredSignature.maxAge must be a positive number of seconds (got: {maxAge})");
            else
                requiredSignature = new RequiredSignature { AcrValues = requiredSignatureRequest.AcrValues, MaxAge = requiredSignatureRequest.MaxAge, EnableRfc3161Timestamp = requiredSignatureRequest.EnableRfc3161Timestamp };
        }

        // ADR-094 -- an empty ResponseEventType or a non-positive Within is
        // always a genuine request mistake (a configuration that could
        // never actually satisfy or ever expire), the same "reject rather
        // than silently accept a no-op" posture RequiredSignature above
        // already established.
        ExpectedResponse? expectedResponse = null;
        if (request.ExpectedResponse is { } expectedResponseRequest)
        {
            if (string.IsNullOrWhiteSpace(expectedResponseRequest.ResponseEventType))
                errors.Add("expectedResponse.responseEventType must be set");
            else if (expectedResponseRequest.Within <= TimeSpan.Zero)
                errors.Add("expectedResponse.within must be a positive duration");
            else
                expectedResponse = new ExpectedResponse { ResponseEventType = expectedResponseRequest.ResponseEventType.ToLowerInvariant(), Within = expectedResponseRequest.Within };
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

        // Npgsql's retrying execution strategy (EventStore.Host.Postgres's
        // EnableRetryOnFailure) forbids a manually-started transaction unless
        // the WHOLE retryable unit -- every read/Add/SaveChanges, not just
        // BeginTransaction/Commit -- runs inside CreateExecutionStrategy's own
        // delegate. Same fix as EventAppender.AppendAsync's own comment
        // explains in full; the full definition (not just newVersion) is
        // returned out of the delegate since AppendSchemaRegisteredAsync's
        // own cross-peer replication payload (below) now needs every field
        // on it, not only the version number.
        var strategy = db.Database.CreateExecutionStrategy();
        var registeredDefinition = await strategy.ExecuteAsync(async () =>
        {
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
                RequiredSignature = requiredSignature,
                ExpectedResponse = expectedResponse,
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
            return definition;
        });

        cache.Remove(OpenApiDocumentCacheKey); // ADR-002 -- ~60s TTL otherwise; invalidate immediately on registration
        cache.Remove(AsyncApiDocumentCacheKey);
        schemaChangeNotifier?.NotifyChanged(); // "GraphQL-Only Query Layer" -- same immediate-invalidation discipline, for the dynamically-built Subscription schema

        // ADR-067 -- a control-plane mutation publishes a reserved, hash-chained
        // audit event, the same treatment ADR-020's EventUpcastFailed already
        // established -- guarded against SchemaRegisteredEventType's own
        // bootstrap registration (its first-ever RegisterAsync call for this
        // AppId, triggered by EnsureRegisteredAsync below), which would
        // otherwise recurse into appending an event about registering the
        // event type this very append needs to already exist.
        if (normalizedName != SchemaRegisteredEventType.Name.ToLowerInvariant())
        {
            await SchemaRegisteredEventType.EnsureRegisteredAsync(this, request.AppId, ct);
            await AppendSchemaRegisteredAsync(registeredDefinition, user, ct);
        }

        return new RegisterEventTypeResult.Success(registeredDefinition.Version);
    }

    // Promoted from Samples.Vitals.VitalsSharedTypes/Samples.Meridian.
    // MeridianSharedTypes's own byte-for-byte-identical
    // EnsureAuthorityDecisionRegisteredAsync methods (TODO.md, "Promote the
    // duplicated ... registration helper into EventStore.SchemaRegistry
    // itself") -- both sample apps register a shared, reactor-named type
    // (EventStore.Router's AuthorityDecisionResolver resolves purely by
    // targetEventId, with no per-AppId or per-domain knowledge of the type
    // name itself) that every workflow needing a human decision on an
    // already-captured record widens with its own Publish-direction claim,
    // ADR-050's OR-of-list semantics (any ONE listed claim satisfies the
    // gate) meaning each caller only ever ADDS to the list, never replaces
    // it. This duplication had a real, observed cost before promotion:
    // Vitals' own copy hardcoded a RequiredSignature parameter that
    // Meridian's copy omitted, so Meridian's Workflow C had to hand-register
    // a wholly separate event type (SarFilingRecorded) instead of reusing
    // this one when it needed step-up sign-off. EntityIdField/ChangeKind/
    // ParentValidationMode are fixed, not parameters -- they're inherent to
    // what "a reserved type AuthorityDecisionResolver can resolve" means
    // (docs/patterns/interactions/claim-gated-step-up-signoff.md), not a
    // per-caller choice the way jsonSchema/requiredPublishClaim/
    // requiredSignature are.
    public async Task EnsureClaimOnReservedTypeAsync(
        string appId, string typeName, string jsonSchema, string requiredPublishClaim,
        RequiredSignatureRequest? requiredSignature = null, CancellationToken ct = default)
    {
        var normalizedName = typeName.ToLowerInvariant();
        var active = await GetActiveAsync(appId, normalizedName, ct);
        var existingClaims = active?.RequiredClaims
            .Where(c => c.Direction == ClaimDirection.Publish)
            .Select(c => c.Claim)
            .ToList() ?? [];
        if (existingClaims.Contains(requiredPublishClaim))
            return; // already covers this caller's own claim -- no new version needed

        var claims = existingClaims.Append(requiredPublishClaim).Distinct()
            .Select(c => new RequiredClaimRequest("Publish", c)).ToList();

        await RegisterAsync(normalizedName, new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: jsonSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.targetEventId", ParentValidationMode: "Permissive",
            RequiredClaims: claims, UpcastFromPrevious: null, DowncastToPrevious: null,
            RequiredSignature: requiredSignature), ct);
    }

    // ADR-033/ADR-067 -- the full registration, not just {EventTypeName,
    // Version} (docs/10-open-questions.md row 1, resolved this pass):
    // widened so a receiving peer's own ApplyReplicatedRegistrationAsync
    // below can actually fold this into a usable EventTypeDefinition,
    // instead of only ever recording that a registration happened
    // somewhere without knowing what it was.
    private async Task AppendSchemaRegisteredAsync(EventTypeDefinition definition, ClaimsPrincipal? user, CancellationToken ct)
    {
        var replicated = new ReplicatedSchemaRegistration(
            AppId: definition.AppId,
            EventTypeName: definition.Name,
            Version: definition.Version,
            JsonSchema: definition.JsonSchema,
            RegisteredAt: definition.RegisteredAt,
            ParentValidationMode: definition.ParentValidationMode.ToString(),
            ChangeKind: definition.ChangeKind.ToString(),
            EntityIdField: definition.EntityIdField,
            EntityType: definition.EntityType,
            UpcastFromPrevious: definition.UpcastFromPrevious,
            DowncastToPrevious: definition.DowncastToPrevious,
            RejectionBehavior: definition.RejectionBehavior.ToString(),
            RequiredClaims: definition.RequiredClaims.Select(c => new ReplicatedRequiredClaim(c.Direction.ToString(), c.Claim)).ToList(),
            FilterableFields: definition.FilterableFields.Select(f => new ReplicatedFilterableField(
                f.JsonPath, f.DataType.ToString(), f.IsIndexed, f.IndexKind.ToString(),
                f.SearchableConfig is null ? null : new ReplicatedSearchableIndexConfig(
                    f.SearchableConfig.IndexKind.ToString(), f.SearchableConfig.KeyScope.ToString(),
                    f.SearchableConfig.BucketGranularities, f.SearchableConfig.Cardinality?.ToString(),
                    f.SearchableConfig.AcknowledgeLeakageRisk))).ToList(),
            RequiredSignature: definition.RequiredSignature is null ? null : new ReplicatedRequiredSignature(
                definition.RequiredSignature.AcrValues, definition.RequiredSignature.MaxAge, definition.RequiredSignature.EnableRfc3161Timestamp),
            ExpectedResponse: definition.ExpectedResponse is null ? null : new ReplicatedExpectedResponse(
                definition.ExpectedResponse.ResponseEventType, definition.ExpectedResponse.Within));
        var payload = JsonSerializer.Serialize(replicated);

        var storedEvent = new StoredEvent
        {
            EventId = Guid.NewGuid(),
            AppId = definition.AppId,
            EntityId = $"{definition.AppId}:schema:{definition.Name}",
            OriginId = _originId, // ADR-033 -- this site's own real, configured identity; was always the literal "local" until this pass, a real bug (see this class's own field comment)
            LogicalClock = "",
            EventType = SchemaRegisteredEventType.Name.ToLowerInvariant(),
            SchemaVersion = 1,
            Payload = payload,
            PayloadHash = EventPayloadHash.Compute(SchemaRegisteredEventType.Name.ToLowerInvariant(), payload, []),
            ChainHash = "",
            Status = "applied", // system-generated and folded immediately, never left "received" for the Router to reprocess (same posture as UpcastMaterializer)
            SchemaStatus = "conformant",
            AuthorityStatus = "accepted",
            OccurredAt = DateTimeOffset.UtcNow,
            // AccessLogReaderContext.Resolve's own claim resolution, reused
            // verbatim (ADR-064) -- "system" when no caller principal was
            // supplied at all (most existing RegisterAsync call sites predate
            // this parameter and never will pass one), distinct from
            // "unauthenticated" (a real caller whose token carried no
            // identifiable claim).
            ActorId = user is null ? "system" : AccessLogReaderContext.Resolve(user).ReaderActorId,
        };
        await EventAppender.AppendAsync(db, storedEvent, [], ct);
    }

    // The peer-sync counterpart to RegisterAsync -- applied when a
    // SchemaRegistered notification event syncs in from ANOTHER site
    // (RouterWorker's own SchemaRegistrationReplicationResolver gates this:
    // never called for this site's own locally-originated copy, which
    // already applied directly via RegisterAsync itself, above). Trusts the
    // origin site's own already-validated decision completely -- no
    // re-validation, no auto-incrementing version (the replicated Version
    // IS the authoritative one), and never appends a second
    // SchemaRegistered notification, which would gossip-amplify forever in
    // a full-mesh topology (ADR-033).
    public async Task ApplyReplicatedRegistrationAsync(ReplicatedSchemaRegistration replicated, CancellationToken ct = default)
    {
        var normalizedName = replicated.EventTypeName.ToLowerInvariant();

        // Already applied -- either an earlier delivery of this exact
        // event (peer-sync's own catch-up/gossip can redeliver), or
        // genuinely received via more than one peer hop in the mesh. A
        // no-op, not an error -- the same idempotency RbacProjectionWorker's
        // own fold target methods (RoleService.AssignRoleAsync etc.)
        // already establish.
        var alreadyApplied = await db.EventTypeDefinitions
            .AnyAsync(e => e.AppId == replicated.AppId && e.Name == normalizedName && e.Version == replicated.Version, ct);
        if (alreadyApplied)
            return;

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Only supersede the currently-Active version if the replicated
            // one is genuinely newer -- an out-of-order/late redelivery of
            // an OLDER version than what's already active here must never
            // regress IsActive back onto it.
            var currentActive = await db.EventTypeDefinitions
                .Where(e => e.AppId == replicated.AppId && e.Name == normalizedName && e.IsActive)
                .SingleOrDefaultAsync(ct);
            var isNewest = currentActive is null || currentActive.Version < replicated.Version;
            if (isNewest && currentActive is not null)
            {
                currentActive.IsActive = false;
                db.EventTypeDefinitions.Update(currentActive);
            }

            var filterableFields = replicated.FilterableFields.Select(f => new FilterableField
            {
                EventTypeAppId = replicated.AppId,
                EventTypeName = normalizedName,
                JsonPath = f.JsonPath,
                DataType = Enum.Parse<FilterableFieldType>(f.DataType),
                IsIndexed = f.IsIndexed,
                IndexKind = Enum.Parse<FilterableFieldIndexKind>(f.IndexKind),
                SearchableConfig = f.SearchableConfig is null ? null : new SearchableIndexConfig
                {
                    IndexKind = Enum.Parse<SearchableIndexKind>(f.SearchableConfig.IndexKind),
                    KeyScope = Enum.Parse<SearchIndexKeyScope>(f.SearchableConfig.KeyScope),
                    BucketGranularities = f.SearchableConfig.BucketGranularities,
                    Cardinality = f.SearchableConfig.Cardinality is null ? null : Enum.Parse<FieldCardinality>(f.SearchableConfig.Cardinality),
                    AcknowledgeLeakageRisk = f.SearchableConfig.AcknowledgeLeakageRisk,
                },
            }).ToList();

            var definition = new EventTypeDefinition
            {
                AppId = replicated.AppId,
                Name = normalizedName,
                Version = replicated.Version,
                JsonSchema = replicated.JsonSchema,
                RegisteredAt = replicated.RegisteredAt,
                IsActive = isNewest,
                ParentValidationMode = Enum.Parse<ParentValidationMode>(replicated.ParentValidationMode),
                RequiredClaims = replicated.RequiredClaims.Select(c => new RequiredClaim { Direction = Enum.Parse<ClaimDirection>(c.Direction), Claim = c.Claim }).ToList(),
                ChangeKind = Enum.Parse<ChangeKind>(replicated.ChangeKind),
                EntityIdField = replicated.EntityIdField,
                EntityType = replicated.EntityType,
                UpcastFromPrevious = replicated.UpcastFromPrevious,
                DowncastToPrevious = replicated.DowncastToPrevious,
                RejectionBehavior = Enum.Parse<RejectionBehavior>(replicated.RejectionBehavior),
                FilterableFields = filterableFields,
                RequiredSignature = replicated.RequiredSignature is null ? null : new RequiredSignature
                {
                    AcrValues = replicated.RequiredSignature.AcrValues,
                    MaxAge = replicated.RequiredSignature.MaxAge,
                    EnableRfc3161Timestamp = replicated.RequiredSignature.EnableRfc3161Timestamp,
                },
                ExpectedResponse = replicated.ExpectedResponse is null ? null : new ExpectedResponse
                {
                    ResponseEventType = replicated.ExpectedResponse.ResponseEventType,
                    Within = replicated.ExpectedResponse.Within,
                },
            };
            db.EventTypeDefinitions.Add(definition);

            await db.SaveChangesAsync(ct);

            // Same provider-specific index creation RegisterAsync performs
            // for its own local registration -- a replicated filterable
            // field must be genuinely queryable identically on this peer,
            // not merely present as metadata.
            foreach (var field in filterableFields.Where(f => f.IsIndexed))
            {
                var indexName = $"IX_Events_{replicated.AppId}_{normalizedName}_{replicated.Version}_{field.Id}";
                var ddlStatements = indexDdlGenerator.GenerateCreateIndexDdl("Events", "Payload", field.JsonPath, indexName);
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
        });

        cache.Remove(OpenApiDocumentCacheKey);
        cache.Remove(AsyncApiDocumentCacheKey);
        schemaChangeNotifier?.NotifyChanged();
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

    // Read-time RequiredClaims lookup, keyed by (AppId, EventType) -- resolves
    // exactly, no tie-break needed. `StoredEvent.AppId` (added by "Entity-Centric
    // Core Rebuild") made this possible; TODO.md tracked the gap between that
    // column existing and Follow/Lineage's own call sites (which always have the
    // StoredEvent in hand) actually reading it, rather than the bare-EventType-
    // name/AppId-ordering-tie-break simplification this replaced.
    public async Task<IReadOnlyList<RequiredClaim>> GetActiveClaimsByAppAndNameAsync(string appId, string eventTypeName, CancellationToken ct = default)
    {
        var definition = await db.EventTypeDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.AppId == appId && e.Name == eventTypeName.ToLowerInvariant() && e.IsActive, ct);
        return definition?.RequiredClaims ?? [];
    }

    // Batch form of GetActiveClaimsByAppAndNameAsync, for Lineage's ancestor/
    // descendant traversal and Follow's per-parent visibility check -- one query
    // for every distinct (AppId, EventType) pair discovered in the reachable set,
    // instead of one round trip per node. No matching active definition at all
    // defaults to "unrestricted" rather than failing closed, so a data
    // inconsistency can't accidentally lock out an otherwise-valid stored event.
    public async Task<IReadOnlyDictionary<(string AppId, string EventType), IReadOnlyList<RequiredClaim>>> GetActiveClaimsByAppAndNamesAsync(
        IReadOnlyCollection<(string AppId, string EventType)> keys, CancellationToken ct = default)
    {
        var normalizedKeys = keys.Select(k => (k.AppId, EventType: k.EventType.ToLowerInvariant())).Distinct().ToList();
        var appIds = normalizedKeys.Select(k => k.AppId).Distinct().ToList();
        var names = normalizedKeys.Select(k => k.EventType).Distinct().ToList();
        var definitions = await db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => appIds.Contains(e.AppId) && names.Contains(e.Name) && e.IsActive)
            .ToListAsync(ct);
        return definitions.ToDictionary(e => (e.AppId, e.Name), e => (IReadOnlyList<RequiredClaim>)e.RequiredClaims);
    }

    // "Hardening & Evolution" -- UpcastChain needs the event type's own
    // CURRENT active version as the upcast destination. AppId-scoped, not
    // the bare-name lookup this originally was -- every actual caller
    // (EventTailReader.TailAsync) has a real AppId in scope (the
    // subscription/Follow request's own AppId), and a bare-name lookup
    // here is what let EventTailReader resolve upcast/downcast against
    // the WRONG AppId's schema definition on a name collision -- the same
    // class of cross-application leak as the AppId-blind event query this
    // pass also fixed, not just a benign open design question.
    public async Task<EventTypeDefinition?> GetActiveDefinitionAsync(string appId, string eventTypeName, CancellationToken ct = default) =>
        await db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => e.AppId == appId && e.Name == eventTypeName.ToLowerInvariant() && e.IsActive)
            .FirstOrDefaultAsync(ct);

    // "CQRS Read-Model Projections" -- ProjectionHost needs each followed
    // event type's ChangeKind (ADR-016) to know Full-replace vs. Partial-merge,
    // but has no direct service/DB reference at all (docs/06-solution-
    // structure.md: "its only dependency on the write side is an HTTP client"),
    // so this must be reachable over HTTP -- see SchemaRegistryEndpoints'
    // new GET /registry/{eventType}/change-kind. Unlike Lineage/Follow's own
    // claims lookups above, this caller genuinely has no AppId to give (a bare
    // eventType path segment, nothing else) -- keeps the deterministic-but-
    // arbitrary AppId-ordering tie-break on a genuine collision.
    public async Task<ChangeKind?> GetActiveChangeKindByNameAsync(string eventTypeName, CancellationToken ct = default)
    {
        var definition = await db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => e.Name == eventTypeName.ToLowerInvariant() && e.IsActive)
            .OrderBy(e => e.AppId)
            .FirstOrDefaultAsync(ct);
        return definition?.ChangeKind;
    }

    // Batch, AppId-and-version lookup for Follow's per-event masking.
    // Masking must use each event's own SchemaVersion, not whichever
    // version is currently active -- a payload's shape always matches the
    // version it was originally validated against. Used to be a bare-name
    // lookup (no AppId) until both of its callers (EventTailReader.
    // TailAsync, FollowService.ConnectAsync) grew an AppId parameter this
    // pass, closing the same cross-application schema-resolution leak
    // GetActiveDefinitionAsync's own comment describes.
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
        // ADR-067 -- SchemaRegistered is a reserved, platform-owned type
        // (never registered via PUT /registry, only ever via
        // EnsureRegisteredAsync's own bootstrap), the same "not something an
        // operator manages" posture ChannelLagDetected/EntityErasureRequested
        // already have -- excluded here so it never silently pads a caller's
        // own listing/pagination of the TYPES THEY registered. Found while
        // testing this item's own new behavior (ListingSupportsTopAndSkip
        // Pagination's count assertion broke the moment any AppId's first-ever
        // registration also became this type's own bootstrap trigger).
        // ChannelLagDetected/EntityErasureRequested are widened in here too,
        // TODO.md's own tracked follow-up -- literal lowercased names, not a
        // reference to ChannelLagDetectedEventType.Name/
        // EntityErasureRequestedEventType.Name: EventStore.Streaming and
        // EventStore.Erasure both already depend on EventStore.SchemaRegistry
        // (their own EnsureRegisteredAsync bootstrap needs it), so referencing
        // either type's own class from here would be a circular project
        // reference. (EventUpcastFailed, TODO.md's third named type, is moot --
        // retired entirely in "Entity-Centric Core Rebuild" per ADR-020's own
        // corrected Consequences; no such class exists anymore to bootstrap-
        // register itself into any AppId's listing in the first place.)
        var reservedTypeNames = new[]
        {
            SchemaRegisteredEventType.Name.ToLowerInvariant(),
            "channellagdetected",
            "entityerasurerequested",
            // ADR-094 -- "never registered via PUT /registry/{event-type},
            // the same treatment EventUpcastFailed gets" -- missed when this
            // list was widened for ChannelLagDetected/EntityErasureRequested;
            // found while building the Event Composer, whose dropdown
            // otherwise lists it as if an operator could hand-register it.
            "expectedresponsemissing",
        };
        var query = db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => e.AppId == appId && !reservedTypeNames.Contains(e.Name))
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

    // ADR-096/097 -- same walk as ResolvesInSchema above, but returns the
    // resolved node itself rather than a bool, so its own x-masking-
    // searchable extension (already structurally validated by
    // MaskingSchemaValidator, walked independently per this design's own
    // "no shared schema walker" pattern) can be read here too.
    private static JsonObject? ResolveSchemaNode(JsonNode schemaNode, string jsonPath)
    {
        JsonNode? current = schemaNode;
        foreach (var segment in JsonPathValidation.Segments(jsonPath))
        {
            if (current is not JsonObject obj || obj["properties"] is not JsonObject properties ||
                !properties.TryGetPropertyValue(segment, out current) || current is null)
                return null;
        }
        return current as JsonObject;
    }

    private static (FilterableFieldIndexKind IndexKind, SearchableIndexConfig? Config) ResolveSearchableIndexKind(JsonNode schemaNode, string jsonPath)
    {
        if (ResolveSchemaNode(schemaNode, jsonPath) is not { } targetNode ||
            targetNode["x-masking-searchable"] is not JsonObject searchable ||
            !Enum.TryParse<SearchableIndexKind>(searchable["indexKind"]?.GetValue<string>(), out var searchableKind))
            return (FilterableFieldIndexKind.PlaintextExpression, null);

        Enum.TryParse<SearchIndexKeyScope>(searchable["keyScope"]?.GetValue<string>(), out var keyScope);
        Enum.TryParse<FieldCardinality>(searchable["cardinality"]?.GetValue<string>(), out var cardinality);
        var config = new SearchableIndexConfig
        {
            IndexKind = searchableKind,
            KeyScope = keyScope,
            BucketGranularities = searchable["bucketGranularities"] is JsonArray granularities
                ? granularities.Select(g => g!.GetValue<string>()).ToList()
                : null,
            Cardinality = searchable["cardinality"] is not null ? cardinality : null,
            AcknowledgeLeakageRisk = searchable["acknowledgeLeakageRisk"]?.GetValue<bool>() ?? false,
        };

        var indexKind = searchableKind switch
        {
            SearchableIndexKind.Equality => FilterableFieldIndexKind.EncryptedBlindIndex,
            SearchableIndexKind.Range => FilterableFieldIndexKind.EncryptedRangeBucket,
            _ => FilterableFieldIndexKind.PlaintextExpression,
        };
        return (indexKind, config);
    }

    private static bool IsTypeValueFormat(string claim)
    {
        var parts = claim.Split(':', 2);
        return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0;
    }
}
