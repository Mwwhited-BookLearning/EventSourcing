using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EventStore.Domain.AccessLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Masking;
using EventStore.Persistence;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.GraphQL;

// docs/10-open-questions.md's row on the generic entity/Live-View query
// ADR-042/045 both assumed but "GraphQL-Only Query Layer" never built --
// built 2026-08-12, direct decision. Mirrors FollowSubscriptionTypeModule's
// own dynamic-per-registered-type schema composition (ADR-037's "a client
// cannot construct a query referencing an undeclared field" guarantee
// applies here too), but keyed by (AppId, EntityType) rather than
// (AppId, EventType) -- an EntityType can be contributed to by several
// distinct event types (e.g. Vitals' IonmAlert entity: IonmAlertRaised +
// IonmAlertAcknowledged), so the dynamic type's own fields/masking rules
// here are a UNION across every event type sharing that EntityType, not
// one type's own schema alone.
//
// Field-building/masking logic intentionally mirrors (does not extract-
// and-share with) FollowSubscriptionTypeModule's own BuildPayloadFields/
// ExtractScalar/BuildMasked -- refactoring that already-tested, heavily-
// exercised file to share code with a brand-new one was judged riskier
// than the duplication, the same RouterWorker.FoldAsync/FoldLiveAsync
// precedent this codebase already accepts elsewhere.
public class EntityQueryTypeModule(IServiceScopeFactory scopeFactory) : ITypeModule
{
    // Same "hot-registration needs a process restart" limitation
    // FollowSubscriptionTypeModule's own header comment documents and
    // TODO.md already tracks -- not re-solved here, this event is simply
    // never raised.
    public event EventHandler<EventArgs>? TypesChanged;

    public async ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(IDescriptorContext context, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EventStoreContext>();
        var activeTypes = await db.EventTypeDefinitions.AsNoTracking().Where(e => e.IsActive).ToListAsync(cancellationToken);

        var types = new List<ITypeSystemMember>();
        var queryExtension = new ObjectTypeConfiguration(OperationTypeNames.Query) { IsExtension = true };

        foreach (var group in activeTypes.GroupBy(d => (d.AppId, d.EntityType)))
        {
            var (appId, entityType) = group.Key;
            var contributing = group.ToList();
            var safeAppId = Sanitize(appId);
            var safeEntityType = Sanitize(entityType.ToLowerInvariant());
            var entityGraphTypeName = $"{safeAppId}_{safeEntityType}_Entity";

            var entityConfig = new ObjectTypeConfiguration(entityGraphTypeName);
            var seenPropertyNames = new HashSet<string>();
            foreach (var definition in contributing)
                foreach (var property in EventTypeSchemaReader.GetTopLevelProperties(definition.JsonSchema))
                    if (seenPropertyNames.Add(property.Name)) // first contributing type wins a name collision -- rare, and the same property name folding the same entity from two types should describe the same logical field anyway
                        foreach (var field in BuildEntityPropertyFields(property))
                            entityConfig.Fields.Add(field);
            foreach (var field in BuildEntityEnvelopeFields())
                entityConfig.Fields.Add(field);
            entityConfig.Fields.Add(BuildAttachmentsField());

            var entityGraphType = ObjectType.CreateUnsafe(entityConfig);
            types.Add(entityGraphType);

            queryExtension.Fields.Add(BuildEntityQueryField(appId, entityType, safeAppId, safeEntityType, entityGraphType, contributing));
        }

        types.Add(ObjectTypeExtension.CreateUnsafe(queryExtension));
        return types;
    }

    private static string FieldNameFor(string jsonPropertyName) =>
        jsonPropertyName.Length == 0 ? jsonPropertyName : char.ToLowerInvariant(jsonPropertyName[0]) + jsonPropertyName[1..];

    private static IEnumerable<ObjectFieldConfiguration> BuildEntityPropertyFields(EventPayloadProperty property)
    {
        var fieldName = FieldNameFor(property.Name);
        if (property.IsMaskable)
        {
            var maskedTypeName = property.Kind switch
            {
                GraphQlScalarKind.Float => "MaskedFloat",
                GraphQlScalarKind.Boolean => "MaskedBoolean",
                GraphQlScalarKind.DateTimeOffset => "MaskedDateTimeOffset",
                _ => "MaskedString",
            };
            yield return new ObjectFieldConfiguration(fieldName, type: TypeReference.Parse(maskedTypeName))
            {
                PureResolver = ctx => BuildMasked(ctx.Parent<EntityQueryResult>().MaskedData, property),
            };
            yield break;
        }

        var scalarName = property.Kind switch
        {
            GraphQlScalarKind.Float => "Float",
            GraphQlScalarKind.Boolean => "Boolean",
            GraphQlScalarKind.DateTimeOffset => "DateTimeOffset",
            _ => "String",
        };
        yield return new ObjectFieldConfiguration(fieldName, type: TypeReference.Parse(scalarName))
        {
            PureResolver = ctx => ExtractScalar(ctx.Parent<EntityQueryResult>().MaskedData?[property.Name], property.Kind),
        };

        if (property.EnumFallback)
        {
            var knownValues = property.KnownValues ?? (IReadOnlySet<string>)new HashSet<string>();
            yield return new ObjectFieldConfiguration($"{fieldName}Known", type: TypeReference.Parse("Boolean"))
            {
                PureResolver = ctx =>
                {
                    var raw = ctx.Parent<EntityQueryResult>().MaskedData?[property.Name]?.GetValue<string>();
                    return raw is not null && knownValues.Contains(raw);
                },
            };
        }
    }

    // isAuthoritative (ADR-042's own headline caller-facing requirement --
    // this open question's entire reason to exist) and authorityStatus are
    // the two fields no per-EVENT envelope already covers. version/
    // schemaVersion/lateArrivalFlag are only ever meaningful once
    // isAuthoritative is true -- the authoritative EntityStore is the only
    // row that tracks them at all (LiveEntityStoreRow deliberately doesn't,
    // per that row's own type comment) -- so they're nullable/default-false
    // rather than throwing when read off the Live View.
    private static IEnumerable<ObjectFieldConfiguration> BuildEntityEnvelopeFields()
    {
        // No "!" non-null wrappers here -- matches FollowSubscriptionTypeModule's
        // own BuildEnvelopeFlagFields precedent exactly (plain "Boolean"/"String"),
        // and "updatedAt" is a plain ISO-8601 "String" rather than the
        // "DateTimeOffset" scalar -- found by actually running this: HotChocolate
        // failed schema build with "Unable to resolve type reference
        // `DateTimeOffset!`" the one time this schema had nothing else that
        // caused that scalar to be bound first, a real ordering/discovery
        // quirk of ITypeModule-built types, not assumed from documentation.
        yield return new ObjectFieldConfiguration("isAuthoritative", type: TypeReference.Parse("Boolean"))
        { PureResolver = ctx => ctx.Parent<EntityQueryResult>().IsAuthoritative };
        yield return new ObjectFieldConfiguration("authorityStatus", type: TypeReference.Parse("String"))
        { PureResolver = ctx => ctx.Parent<EntityQueryResult>().AuthorityStatus };
        yield return new ObjectFieldConfiguration("version", type: TypeReference.Parse("Long"))
        { PureResolver = ctx => ctx.Parent<EntityQueryResult>().Version };
        yield return new ObjectFieldConfiguration("schemaVersion", type: TypeReference.Parse("Int"))
        { PureResolver = ctx => ctx.Parent<EntityQueryResult>().SchemaVersion };
        yield return new ObjectFieldConfiguration("lateArrivalFlag", type: TypeReference.Parse("Boolean"))
        { PureResolver = ctx => ctx.Parent<EntityQueryResult>().LateArrivalFlag };
        yield return new ObjectFieldConfiguration("updatedAt", type: TypeReference.Parse("String"))
        { PureResolver = ctx => ctx.Parent<EntityQueryResult>().UpdatedAt.ToString("O") };
    }

    // ADR-032's own Decision, verbatim: "entity(id) { attachments {
    // contentHash, filename, mimeType, sizeBytes } }" -- TODO.md had
    // flagged this as documented/Gherkin-tested but never built (no
    // entity(id) field existed at all at the time). Attached to every
    // dynamically-built entity type below, resolving AttachmentRef rows
    // linked to THIS entity's own EntityId, joined to Attachment for the
    // listed fields. A single shared Attachment output type, registered
    // once in the composition root (GraphQlServiceCollectionExtensions),
    // not duplicated per entity type the way entityConfig itself is.
    private static ObjectFieldConfiguration BuildAttachmentsField() =>
        new("attachments", type: TypeReference.Parse("[Attachment!]!"))
        {
            Resolver = async ctx => await ResolveAttachmentsAsync(ctx),
        };

    private static async ValueTask<object> ResolveAttachmentsAsync(IResolverContext ctx)
    {
        var db = ctx.Service<EventStoreContext>();
        var user = ctx.Service<ClaimsPrincipal>();
        var entityId = ctx.Parent<EntityQueryResult>().EntityId;

        var linked = await db.AttachmentRefs.AsNoTracking()
            .Where(r => r.EntityId == entityId)
            .Join(db.Attachments.AsNoTracking(), r => r.ContentHash, a => a.ContentHash,
                (r, a) => new { a.ContentHash, a.FileName, a.MimeType, a.SizeBytes, a.RequiredReadClaim })
            .ToListAsync(ctx.RequestAborted);

        // ADR-032 -- "a direct claim on the attachment always governs if
        // set," the same check AttachmentEndpoints' own GET enforces for
        // byte retrieval; here it silently excludes an inaccessible
        // attachment from the list rather than forbidding the whole
        // query, since this is a listing of POTENTIALLY many attachments
        // with independent, per-item claims, not a single-resource read.
        return linked
            .Where(a => a.RequiredReadClaim is null || RequiredClaimEvaluator.HasClaim(user, a.RequiredReadClaim))
            .Select(a => new Attachment(a.ContentHash, a.FileName, a.MimeType, a.SizeBytes))
            .ToList();
    }

    private static object? BuildMasked(JsonObject? payload, EventPayloadProperty property)
    {
        if (payload?[property.Name] is not JsonObject wrapper)
            return null;

        var value = wrapper["value"] is { } v ? ExtractScalar(v, property.Kind) : null;
        var masked = wrapper["masked"]?.GetValue<string>();
        var erased = wrapper["erased"]?.GetValue<bool>();

        return property.Kind switch
        {
            GraphQlScalarKind.Float => new MaskedFloat((double?)value, masked, erased),
            GraphQlScalarKind.Boolean => new MaskedBoolean((bool?)value, masked, erased),
            GraphQlScalarKind.DateTimeOffset => new MaskedDateTimeOffset((DateTimeOffset?)value, masked, erased),
            _ => new MaskedString((string?)value, masked, erased),
        };
    }

    private static object? ExtractScalar(JsonNode? node, GraphQlScalarKind kind)
    {
        if (node is null)
            return null;
        return kind switch
        {
            GraphQlScalarKind.Float => node.GetValue<double>(),
            GraphQlScalarKind.Boolean => node.GetValue<bool>(),
            GraphQlScalarKind.DateTimeOffset => node.GetValue<DateTimeOffset>(),
            _ => node.GetValue<string>(),
        };
    }

    private ObjectFieldConfiguration BuildEntityQueryField(
        string appId, string entityType, string safeAppId, string safeEntityType, ObjectType entityGraphType, List<EventTypeDefinition> contributing)
    {
        var fieldName = $"entity_{safeAppId}_{safeEntityType}";
        var config = new ObjectFieldConfiguration(fieldName, type: TypeReference.Create(entityGraphType))
        {
            Resolver = async ctx => await ResolveEntityAsync(ctx, appId, entityType, contributing),
        };
        config.Arguments.Add(new ArgumentConfiguration("id", type: TypeReference.Parse("String!")));
        return config;
    }

    private static async ValueTask<object?> ResolveEntityAsync(
        IResolverContext ctx, string appId, string entityType, IReadOnlyList<EventTypeDefinition> contributing)
    {
        var db = ctx.Service<EventStoreContext>();
        var user = ctx.Service<ClaimsPrincipal>();
        var authorizationService = ctx.Service<IAuthorizationService>();
        var payloadMasker = ctx.Service<IPayloadMasker>();

        await GraphQlAuth.RequireScopeAsync(authorizationService, user, "events:follow");

        // ADR-008/050 -- union of every contributing event type's own
        // Read-direction claim list, since this ONE entity can be folded
        // from several distinct event types; unrestricted (the common
        // case) when none of them declare one, the same "empty list means
        // unrestricted" default RequiredClaimEvaluator.HasAny already
        // establishes for a single type.
        var readClaims = contributing.SelectMany(d => d.RequiredClaims.Where(c => c.Direction == ClaimDirection.Read)).ToList();
        if (!RequiredClaimEvaluator.HasAny(readClaims, ClaimDirection.Read, user))
            throw new GraphQLException("Forbidden -- caller lacks the required Read claim for this entity type.");

        var id = ctx.ArgumentValue<string>("id");
        var entityId = $"{appId}:{entityType.ToLowerInvariant()}:{id}";

        // ADR-042 -- the authoritative Entity Store, when it exists, wins;
        // otherwise fall back to the always-populated Live View. Reading
        // BOTH unconditionally would be wasted work once the authoritative
        // row already exists.
        var authoritative = await db.EntityStore.AsNoTracking().SingleOrDefaultAsync(r => r.EntityId == entityId, ctx.RequestAborted);
        var live = authoritative is null
            ? await db.LiveEntityStore.AsNoTracking().SingleOrDefaultAsync(r => r.EntityId == entityId, ctx.RequestAborted)
            : null;
        if (authoritative is null && live is null)
            return null;

        var dataJson = authoritative?.Data ?? live!.Data;
        var mergedSchema = BuildMergedSchema(contributing);
        var maskedData = await payloadMasker.MaskAsync(
            mergedSchema, JsonNode.Parse(dataJson), entityId, claim => RequiredClaimEvaluator.HasClaim(user, claim), ctx.RequestAborted) as JsonObject;

        // ADR-045 -- "every GraphQL query against the authoritative Entity
        // Store or Live View" gets an AccessLogEntry, the exact surface
        // this ADR's own text named and this query is what finally builds.
        var (readerActorId, readerTrustBasis, grantRef) = AccessLogReaderContext.Resolve(user);
        await AccessLogAppender.AppendAsync(
            db, readerActorId, readerTrustBasis, grantRef,
            authoritative is not null ? "Authoritative" : "Live", entityId, "read", ctx.RequestAborted);

        return new EntityQueryResult(
            EntityId: entityId,
            IsAuthoritative: authoritative is not null,
            AuthorityStatus: authoritative?.AuthorityStatus ?? live!.AuthorityStatus,
            Version: authoritative?.Version,
            SchemaVersion: authoritative?.SchemaVersion,
            LateArrivalFlag: authoritative?.LateArrivalFlag ?? false,
            UpdatedAt: authoritative?.UpdatedAt ?? live!.UpdatedAt,
            MaskedData: maskedData);
    }

    // A synthesized schema object -- {"type":"object","properties": {...}} --
    // unioning every contributing event type's own top-level `properties`,
    // so PayloadMasker.MaskAsync (which walks ONE schema tree) can mask a
    // merged, multi-source EntityStore/LiveEntityStore Data blob correctly,
    // regardless of which of several event types originally contributed any
    // given field. First contributing definition wins a name collision.
    private static JsonNode BuildMergedSchema(IReadOnlyList<EventTypeDefinition> contributing)
    {
        var properties = new JsonObject();
        foreach (var definition in contributing)
        {
            if (JsonNode.Parse(definition.JsonSchema) is JsonObject schemaObject && schemaObject["properties"] is JsonObject definitionProperties)
                foreach (var (name, value) in definitionProperties)
                    if (!properties.ContainsKey(name))
                        properties[name] = value?.DeepClone();
        }
        return new JsonObject { ["type"] = "object", ["properties"] = properties };
    }

    private static string Sanitize(string value) => Regex.Replace(value, "[^A-Za-z0-9]", "_");
}

internal record EntityQueryResult(
    string EntityId, bool IsAuthoritative, string AuthorityStatus, long? Version, int? SchemaVersion,
    bool LateArrivalFlag, DateTimeOffset UpdatedAt, JsonObject? MaskedData);
