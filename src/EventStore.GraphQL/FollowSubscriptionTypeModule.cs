using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EventStore.Domain.SchemaRegistry;
using EventStore.Follow.Api;
using EventStore.Persistence;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.GraphQL;

// docs/03-api-contracts.md, "Follow -- GraphQL Subscription over SSE
// (ADR-037)": the one genuinely dynamic surface in this item -- unlike
// Lineage/Registry (fixed shape regardless of event type), a Subscription
// field's own payload has a different shape PER REGISTERED EVENT TYPE, so
// "a client literally cannot construct a query referencing an undeclared
// field" (ADR-037's own guarantee) needs real per-type schema composition,
// not just static C# resolvers. Built via HotChocolate's ITypeModule hot-
// reload mechanism (chillicream.com/docs/hotchocolate/v16/defining-a-schema/
// dynamic-schemas) -- verified against the actual installed v16 assembly via
// reflection before writing this, not assumed from an older-version doc
// sample (ObjectTypeDefinition/ObjectFieldDefinition were renamed
// ObjectTypeConfiguration/ObjectFieldConfiguration between the versions
// searched and v16 actually installed here).
//
// GraphQL type/field names are qualified with AppId (on_{appId}_{name}), a
// deliberate, honestly-flagged narrowing from ADR-037's literal "composed
// per AppId... never one fixed global SDL": two different AppIds can
// register the same event-type Name independently (ADR-030), which a
// single shared GraphQL schema cannot express as two identically-named
// types -- this qualifies names to avoid that real collision within ONE
// shared schema/endpoint, rather than provisioning a genuinely separate
// SDL document per AppId (HotChocolate's own multiple-named-schema feature
// is configured at startup for a fixed set of names, not for AppIds
// discovered dynamically at runtime).
public class FollowSubscriptionTypeModule : ITypeModule, ISchemaChangeNotifier
{
    // A found, real limitation, not silently worked around -- see
    // 08-build-plan.md's "GraphQL-Only Query Layer" Built-scope note for the
    // full account: registering a NEW event type while a Host is already
    // running does not currently make its Subscription field appear without
    // a process restart. Confirmed, by direct debugging, that this is NOT a
    // registration bug (a parallel, independent EventStoreContext against
    // the same file sees a freshly-committed registration immediately) --
    // CreateTypesAsync below only ever runs once, at schema warmup, no
    // matter how many times ISchemaChangeNotifier.NotifyChanged() fires
    // afterward or how long a periodic-timer fallback is given to retry
    // (both tried; neither triggered a second invocation in extensive real
    // testing). Deliberately NOT implementing IDisposable here either --
    // this class held a System.Threading.Timer as a first attempt at a
    // periodic-refresh fallback, and the timer never fired more than once;
    // the most likely explanation is that whatever HotChocolate-internal
    // scope builds the schema disposes the type modules it resolves once
    // building finishes, which would silently stop a Timer field even
    // though this same instance keeps living as the app's own long-lived
    // ISchemaChangeNotifier singleton -- so nothing here holds an
    // IDisposable resource that a premature Dispose() call could break.
    private readonly IServiceScopeFactory _scopeFactory;

    public FollowSubscriptionTypeModule(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public event EventHandler<EventArgs>? TypesChanged;

    // ISchemaChangeNotifier -- invoked by SchemaRegistryService right after a
    // successful registration (the same "invalidate immediately" discipline
    // ADR-002's OpenAPI/AsyncAPI cache already uses). Kept even though this
    // pass could not get it to actually trigger a rebuild against an
    // already-running Host -- real, structurally correct, and the interface
    // this class is expected to implement; the gap is on HotChocolate's own
    // reload-scheduling side, not in whether this fires or reaches a
    // subscriber (both confirmed true by direct debugging).
    //
    // TODO.md's own next-suggested-step -- also calling
    // IRequestExecutorManager.EvictExecutor(ISchemaDefinition.DefaultName)
    // here -- was tried and reverted, this same session: it DOES close the
    // gap in isolation (confirmed: a type registered after Host warmup
    // became queryable on the very next request, no restart), but running
    // it alongside this class's OTHER, concurrently-executing tests
    // (MSTestSettings.cs runs every test method in parallel by default)
    // surfaced a materially worse bug than the one it fixed: evicting the
    // cached executor while a DIFFERENT test's Follow subscription is
    // still connected can rebuild the schema mid-flight and cross-deliver
    // an event published under one AppId to a subscription's own dynamic,
    // AppId-qualified field for a DIFFERENT AppId (reproduced concretely --
    // SubscribingOverRealHttpStreamsAMatchingEventAsSse's own
    // "graphql-http-demo-5"-scoped subscription received
    // "graphql-http-demo-2"'s own published OrderPlaced payload). A schema
    // rebuild is not safe to trigger while ANY subscription may be live
    // against the current executor, and this codebase has no mechanism to
    // know that. Do not re-add EvictExecutor here without first solving
    // THAT problem -- it is a materially harder one than "hot-register a
    // new type."
    public void NotifyChanged() => TypesChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(IDescriptorContext context, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EventStoreContext>();
        var activeTypes = await db.EventTypeDefinitions
            .AsNoTracking()
            .Include(e => e.FilterableFields)
            .Where(e => e.IsActive)
            .ToListAsync(cancellationToken);

        var types = new List<ITypeSystemMember>();
        var subscriptionExtension = new ObjectTypeConfiguration(OperationTypeNames.Subscription) { IsExtension = true };

        foreach (var definition in activeTypes)
        {
            var safeAppId = Sanitize(definition.AppId);
            var safeName = Sanitize(definition.Name);
            var payloadTypeName = $"{safeAppId}_{safeName}_Payload";

            var payloadConfig = new ObjectTypeConfiguration(payloadTypeName);
            foreach (var property in EventTypeSchemaReader.GetTopLevelProperties(definition.JsonSchema))
                foreach (var field in BuildPayloadFields(property))
                    payloadConfig.Fields.Add(field);
            foreach (var field in BuildEnvelopeFlagFields())
                payloadConfig.Fields.Add(field);
            var payloadType = ObjectType.CreateUnsafe(payloadConfig);
            types.Add(payloadType);

            subscriptionExtension.Fields.Add(BuildSubscriptionField(definition, safeAppId, safeName, payloadType));
        }

        types.Add(ObjectTypeExtension.CreateUnsafe(subscriptionExtension));
        return types;
    }

    // HotChocolate's own naming convention lower-cases a static C# PascalCase
    // property's first letter for its GraphQL field name (e.g. OrderId ->
    // orderId) -- these dynamically-built fields need the identical
    // conversion applied explicitly, since ObjectFieldConfiguration's own
    // name is used verbatim, with no such convention applied automatically
    // for a field never backed by a real reflected member.
    private static string FieldNameFor(string jsonPropertyName) =>
        jsonPropertyName.Length == 0 ? jsonPropertyName : char.ToLowerInvariant(jsonPropertyName[0]) + jsonPropertyName[1..];

    // Returns one field for an ordinary/masked property, or two -- the value
    // field plus a sibling "{name}Known" Boolean -- for an x-enum-fallback
    // property (ADR-038's enum-fallback contract, docs/features/
    // compatibility-and-versioning.md's Scenario 1: "the response should
    // equal { status, statusKnown }"). EnumFallbackSchemaValidator already
    // guarantees x-enum-fallback and x-masking never both apply to the same
    // property, so the two branches below never need to combine.
    private static IEnumerable<ObjectFieldConfiguration> BuildPayloadFields(EventPayloadProperty property)
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
                PureResolver = ctx => BuildMasked(ctx.Parent<FollowedEvent>().MaskedPayload as JsonObject, property),
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
            PureResolver = ctx => ExtractScalar((ctx.Parent<FollowedEvent>().MaskedPayload as JsonObject)?[property.Name], property.Kind),
        };

        if (property.EnumFallback)
        {
            var knownValues = property.KnownValues ?? (IReadOnlySet<string>)new HashSet<string>();
            yield return new ObjectFieldConfiguration($"{fieldName}Known", type: TypeReference.Parse("Boolean"))
            {
                PureResolver = ctx =>
                {
                    var raw = (ctx.Parent<FollowedEvent>().MaskedPayload as JsonObject)?[property.Name]?.GetValue<string>();
                    return raw is not null && knownValues.Contains(raw);
                },
            };
        }
    }

    // "MVVM Client" (ADR-039) -- ConflictFlag/LateArrivalFlag/AuthorityStatus
    // (ADR-024/029/035) and SchemaVersion are fixed envelope fields every
    // dynamically-built payload type gets, regardless of the event type's
    // own JSON Schema properties -- a client rendering the shared generic
    // "flag" convention, or picking a ViewDefinition version compatible
    // with the event it just received, needs these on every Subscription
    // payload, not just the per-schema-declared ones BuildPayloadFields
    // already handles. Sourced directly from FollowedEvent.Event (the
    // underlying StoredEvent), never from the masked/JSON payload.
    private static IEnumerable<ObjectFieldConfiguration> BuildEnvelopeFlagFields()
    {
        // Added 2026-08-12, "Domain Decision Queues" -- the one envelope
        // field every OTHER Subscription consumer had never needed: a
        // caller that wants to correlate a LATER event back to this one
        // (an authorityDecision's own targetEventId, RespondsToEventId,
        // ADR-094) had no way to discover this event's own EventId at all
        // before this field existed, confirmed by grepping this whole file
        // for "EventId" and finding zero hits. String, not the raw Guid --
        // every other scalar field on this dynamic type already returns a
        // JSON-friendly primitive, never a .NET type GraphQL would need a
        // custom scalar for.
        yield return new ObjectFieldConfiguration("eventId", type: TypeReference.Parse("String"))
        {
            PureResolver = ctx => ctx.Parent<FollowedEvent>().Event.EventId.ToString(),
        };
        // TODO.md's own persisted-resume-cursor gap -- a client needs this
        // to know where to reconnect from (mode: REPLAY, fromSequenceNumber:
        // <this value>; EventTailReader.TailAsync's predicate is
        // SequenceNumber > lastSeen, so the value itself, not +1, is the
        // correct reconnect argument) without re-downloading its entire
        // history on every reconnect. String, not the built-in "Long"
        // scalar this field's own fromSequenceNumber ARGUMENT already uses
        // -- an early version of this field returned a raw long output and
        // appeared to hang the whole SSE stream, but that was a
        // misdiagnosis: the real causes were an off-by-one in the proving
        // test's own reconnect math (see above) compounding with two
        // separate, real bugs this same pass also found and fixed --
        // EventTailReader.TailAsync's missing AppId filter (a cross-
        // application event leak, event type names are not globally
        // unique) and this class's own documented hot-reload gap (a type
        // registered after the schema's first build never gets a
        // subscription field). String is kept anyway, on its own merits --
        // the correct choice regardless for a 64-bit sequence number
        // reaching a JS client, which cannot represent integers above 2^53
        // exactly, the same reasoning behind GraphQL's own common
        // "int64-as-string" convention.
        yield return new ObjectFieldConfiguration("sequenceNumber", type: TypeReference.Parse("String"))
        {
            PureResolver = ctx => ctx.Parent<FollowedEvent>().Event.SequenceNumber.ToString(),
        };
        yield return new ObjectFieldConfiguration("conflictFlag", type: TypeReference.Parse("Boolean"))
        {
            PureResolver = ctx => ctx.Parent<FollowedEvent>().Event.ConflictFlag,
        };
        yield return new ObjectFieldConfiguration("lateArrivalFlag", type: TypeReference.Parse("Boolean"))
        {
            PureResolver = ctx => ctx.Parent<FollowedEvent>().Event.LateArrivalFlag,
        };
        yield return new ObjectFieldConfiguration("authorityStatus", type: TypeReference.Parse("String"))
        {
            PureResolver = ctx => ctx.Parent<FollowedEvent>().Event.AuthorityStatus,
        };
        yield return new ObjectFieldConfiguration("schemaVersion", type: TypeReference.Parse("Int"))
        {
            PureResolver = ctx => ctx.Parent<FollowedEvent>().Event.SchemaVersion,
        };
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

    private ObjectFieldConfiguration BuildSubscriptionField(EventTypeDefinition definition, string safeAppId, string safeName, ObjectType payloadType)
    {
        var fieldName = $"on_{safeAppId}_{safeName}";
        var normalizedEventTypeName = definition.Name;
        var appId = definition.AppId;

        var config = new ObjectFieldConfiguration(fieldName, type: TypeReference.Create(payloadType))
        {
            Resolver = ctx => new ValueTask<object?>(ctx.GetEventMessage<FollowedEvent>()),
            SubscribeResolver = async ctx => await SubscribeAsync(ctx, appId, normalizedEventTypeName),
        };
        config.Arguments.Add(new ArgumentConfiguration("where", type: TypeReference.Parse("[EventFilterInput!]")));
        config.Arguments.Add(new ArgumentConfiguration("mode", type: TypeReference.Parse("FollowMode")) { RuntimeDefaultValue = FollowMode.Tail });
        config.Arguments.Add(new ArgumentConfiguration("fromSequenceNumber", type: TypeReference.Parse("Long")));
        return config;
    }

    private static async ValueTask<HotChocolate.Execution.ISourceStream> SubscribeAsync(IResolverContext ctx, string appId, string normalizedEventTypeName)
    {
        var db = ctx.Service<EventStoreContext>();
        var tailReader = ctx.Service<EventTailReader>();
        var user = ctx.Service<ClaimsPrincipal>();
        var authorizationService = ctx.Service<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

        await GraphQlAuth.RequireScopeAsync(authorizationService, user, "events:follow");

        var definition = await db.EventTypeDefinitions
            .Include(e => e.FilterableFields)
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.AppId == appId && e.Name == normalizedEventTypeName && e.IsActive, ctx.RequestAborted)
            ?? throw new GraphQLException("This event type is no longer registered.");

        // ADR-008/050 -- checked once, at connect time, same as FollowService.ConnectAsync.
        if (!RequiredClaimEvaluator.HasAny(definition.RequiredClaims, ClaimDirection.Read, user))
            throw new GraphQLException("Forbidden -- caller lacks the required Read claim for this event type.");

        var mode = ctx.ArgumentValue<FollowMode?>("mode") ?? FollowMode.Tail;
        var fromSequenceNumber = ctx.ArgumentValue<long?>("fromSequenceNumber");
        var whereClauses = ctx.ArgumentValue<IReadOnlyList<EventFilterInput>?>("where");

        var predicate = GraphQlFilterPredicateBuilder.Build(definition.FilterableFields, whereClauses);

        long lastSeen = mode == FollowMode.Replay
            ? fromSequenceNumber ?? 0
            : await db.Events.AsNoTracking().MaxAsync(e => (long?)e.SequenceNumber, ctx.RequestAborted) ?? 0;

        var events = tailReader.TailAsync(appId, normalizedEventTypeName, predicate, lastSeen, asOfSchemaVersion: null, TimeSpan.FromMilliseconds(200), user, ctx.RequestAborted);
        return new FollowedEventSourceStream(events);
    }

    private static string Sanitize(string value) => Regex.Replace(value, "[^A-Za-z0-9]", "_");

    // HotChocolate.Utilities.Subscriptions.SourceStreamWrapper (the obvious,
    // doc-shown wrapper) turned out to be internal to HotChocolate.Types when
    // checked against the actual installed v16 assembly via reflection --
    // ISourceStream itself is a one-method public interface, trivial to
    // implement directly rather than depend on an inaccessible internal type.
    private sealed class FollowedEventSourceStream(IAsyncEnumerable<FollowedEvent> events) : HotChocolate.Execution.ISourceStream
    {
        public IAsyncEnumerable<object?> ReadEventsAsync()
        {
            return Cast();

            async IAsyncEnumerable<object?> Cast()
            {
                await foreach (var item in events)
                    yield return item;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
