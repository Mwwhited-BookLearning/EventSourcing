using EventStore.Domain.AccessLog;
using EventStore.Domain.EntityStore;
using EventStore.Domain.EventLog;
using EventStore.Domain.FeatureFlags;
using EventStore.Domain.LeaderElection;
using EventStore.Domain.Replication;
using EventStore.Domain.SchemaRegistry;
using EventStore.Domain.Streaming;
using EventStore.Domain.Views;
using EventStore.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EventStore.Persistence;

// IJsonPathTranslator is a required constructor dependency (not just registered in
// DI for endpoints to resolve) because OnModelCreating -- called once per (context
// type, provider) model-cache-key, see ProviderAwareModelCacheKeyFactory below --
// needs a concrete translator instance to close over when registering the four
// JsonFunctions DbFunctions. ASP.NET Core's AddDbContext resolves this
// automatically from the same DI container; anything constructing this context
// directly (every EventStore.IntegrationTests provider test class) must now pass
// the matching provider's translator explicitly.
public class EventStoreContext(DbContextOptions<EventStoreContext> options, IJsonPathTranslator jsonPathTranslator) : DbContext(options)
{
    public DbSet<EventTypeDefinition> EventTypeDefinitions => Set<EventTypeDefinition>();
    public DbSet<FilterableField> FilterableFields => Set<FilterableField>();
    public DbSet<StoredEvent> Events => Set<StoredEvent>();
    public DbSet<EventParent> EventParents => Set<EventParent>();
    public DbSet<DerivationDefinition> DerivationDefinitions => Set<DerivationDefinition>();
    public DbSet<DerivationCursor> DerivationCursors => Set<DerivationCursor>();
    public DbSet<PendingJoinState> PendingJoinStates => Set<PendingJoinState>();
    public DbSet<EntityStoreRow> EntityStore => Set<EntityStoreRow>();
    public DbSet<LiveEntityStoreRow> LiveEntityStore => Set<LiveEntityStoreRow>();
    public DbSet<TelemetryChannel> TelemetryChannels => Set<TelemetryChannel>();
    public DbSet<TelemetrySample> TelemetrySamples => Set<TelemetrySample>();
    public DbSet<RedactedRange> RedactedRanges => Set<RedactedRange>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<AttachmentRef> AttachmentRefs => Set<AttachmentRef>();
    public DbSet<PeerSyncCursor> PeerSyncCursors => Set<PeerSyncCursor>();
    public DbSet<AppResidencyPolicy> AppResidencyPolicies => Set<AppResidencyPolicy>(); // ADR-061
    public DbSet<ViewDefinition> ViewDefinitions => Set<ViewDefinition>();
    public DbSet<AccessLogEntry> AccessLogEntries => Set<AccessLogEntry>();
    public DbSet<EntityErasureKey> EntityErasureKeys => Set<EntityErasureKey>();
    public DbSet<LocalErasureKeyMaterial> LocalErasureKeyMaterials => Set<LocalErasureKeyMaterial>();
    public DbSet<FeatureFlagState> FeatureFlags => Set<FeatureFlagState>(); // ADR-077
    public DbSet<LeaderLease> LeaderLeases => Set<LeaderLease>(); // ADR-078
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>(); // ADR-060
    public DbSet<WebhookOutbox> WebhookOutbox => Set<WebhookOutbox>();
    public DbSet<WebhookDeliveryCursor> WebhookDeliveryCursors => Set<WebhookDeliveryCursor>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, ProviderAwareModelCacheKeyFactory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        RegisterJsonPathFunction(modelBuilder, (Func<string, string, string>)JsonFunctions.JsonValueAsString, FilterableFieldType.String);
        RegisterJsonPathFunction(modelBuilder, (Func<string, string, double>)JsonFunctions.JsonValueAsNumber, FilterableFieldType.Number);
        RegisterJsonPathFunction(modelBuilder, (Func<string, string, bool>)JsonFunctions.JsonValueAsBoolean, FilterableFieldType.Boolean);
        RegisterJsonPathFunction(modelBuilder, (Func<string, string, DateTimeOffset>)JsonFunctions.JsonValueAsDateTimeOffset, FilterableFieldType.DateTimeOffset);

        modelBuilder.Entity<EventTypeDefinition>(e =>
        {
            e.HasKey(x => new { x.AppId, x.Name, x.Version });

            e.Property(x => x.JsonSchema).IsRequired(); // portable TEXT/nvarchar(max) -- never a native JSON column type (ADR-004)

            e.Property(x => x.RequiredClaims)
                .HasConversion(JsonValueConverter.For<List<RequiredClaim>>())
                .Metadata.SetValueComparer(JsonValueConverter.ListComparer<List<RequiredClaim>>());

            e.Property(x => x.RequiredSignature)
                .HasConversion(JsonValueConverter.ForNullable<RequiredSignature>())
                .Metadata.SetValueComparer(JsonValueConverter.NullableComparer<RequiredSignature>());

            e.HasMany(x => x.FilterableFields)
                .WithOne()
                .HasForeignKey(f => new { f.EventTypeAppId, f.EventTypeName, f.EventTypeVersion })
                .HasPrincipalKey(x => new { x.AppId, x.Name, x.Version });
        });

        modelBuilder.Entity<FilterableField>(e =>
        {
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<StoredEvent>(e =>
        {
            e.HasKey(x => x.SequenceNumber);
            e.HasIndex(x => x.EventId).IsUnique(); // ADR-011 -- publish idempotency relies on this constraint existing
            e.HasIndex(x => x.EntityId); // ADR-021 -- QUERY /entities/{entityId}/events and the fold step's own per-entity lookups

            e.Property(x => x.Payload).IsRequired(); // portable TEXT/nvarchar(max)/text -- never a native JSON column type (ADR-004)

            e.Property(x => x.Signature)
                .HasConversion(JsonValueConverter.ForNullable<Signature>())
                .Metadata.SetValueComparer(JsonValueConverter.NullableComparer<Signature>());
        });

        modelBuilder.Entity<EventParent>(e =>
        {
            // Soft references, deliberately no FK constraint (ADR-005) -- a Permissive
            // event type may name a ParentEventId that doesn't resolve to any StoredEvent yet.
            e.HasKey(x => new { x.ChildEventId, x.ParentEventId });
            e.HasIndex(x => x.ChildEventId);
            e.HasIndex(x => x.ParentEventId);
        });

        modelBuilder.Entity<DerivationDefinition>(e =>
        {
            e.HasKey(x => new { x.AppId, x.Name });

            e.Property(x => x.Sources)
                .HasConversion(JsonValueConverter.For<List<string>>())
                .Metadata.SetValueComparer(JsonValueConverter.ListComparer<List<string>>());

            e.Property(x => x.JoinConditions)
                .HasConversion(JsonValueConverter.For<List<JoinCondition>>())
                .Metadata.SetValueComparer(JsonValueConverter.ListComparer<List<JoinCondition>>());

            e.Property(x => x.SelectFields)
                .HasConversion(JsonValueConverter.For<List<SelectField>>())
                .Metadata.SetValueComparer(JsonValueConverter.ListComparer<List<SelectField>>());
        });

        modelBuilder.Entity<DerivationCursor>(e =>
        {
            e.HasKey(x => new { x.AppId, x.DerivationName, x.SourceEventType });
        });

        modelBuilder.Entity<PendingJoinState>(e =>
        {
            e.HasKey(x => x.Id);
            // Not unique: "at most one ACTIVE (ExpiredReason IS NULL) row per key" is
            // an application-level invariant (DerivationWorker always queries
            // ExpiredReason == null before deciding insert-vs-update), not a database
            // constraint -- a straggling source arriving after a key's row already
            // expired starts a fresh row with the same (AppId, DerivationName,
            // JoinKeyValue), which a DB-level unique constraint would reject.
            e.HasIndex(x => new { x.AppId, x.DerivationName, x.JoinKeyValue });
            e.HasIndex(x => x.ExpiresAt); // the TTL sweep's own lookup (ADR-007)
        });

        modelBuilder.Entity<EntityStoreRow>(e =>
        {
            e.HasKey(x => x.EntityId);
            e.HasIndex(x => x.EntityType); // the whole-store, per-entity-type rebuild replay (ADR-021)
        });

        modelBuilder.Entity<LiveEntityStoreRow>(e =>
        {
            e.HasKey(x => x.EntityId);
            e.HasIndex(x => x.EntityType);
        });

        modelBuilder.Entity<TelemetryChannel>(e =>
        {
            e.HasKey(x => x.ChannelId);
            e.HasIndex(x => x.ThreadId); // ADR-081 -- the ThreadId-scoped grouped session read

            e.Property(x => x.SourceChannelIds)
                .HasConversion(JsonValueConverter.ForNullable<List<string>>())
                .Metadata.SetValueComparer(JsonValueConverter.ListComparer<List<string>>());
        });

        modelBuilder.Entity<TelemetrySample>(e =>
        {
            e.HasKey(x => new { x.ChannelId, x.Timestamp });

            e.Property(x => x.Value).IsRequired();
        });

        modelBuilder.Entity<RedactedRange>(e =>
        {
            e.HasKey(x => new { x.ChannelId, x.FromTimestamp });
        });

        modelBuilder.Entity<Attachment>(e =>
        {
            e.HasKey(x => x.ContentHash);

            e.Property(x => x.ChunkIndex)
                .HasConversion(JsonValueConverter.ForNullable<List<ChunkRef>>())
                .Metadata.SetValueComparer(JsonValueConverter.ListComparer<List<ChunkRef>>());
        });

        modelBuilder.Entity<AttachmentRef>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ContentHash); // ADR-032 -- an attachment is many-to-many with events/entities by construction
            e.HasIndex(x => x.EntityId);
            e.HasIndex(x => x.EventId);
        });

        modelBuilder.Entity<PeerSyncCursor>(e =>
        {
            e.HasKey(x => x.PeerId);
        });

        modelBuilder.Entity<AppResidencyPolicy>(e =>
        {
            e.HasKey(x => x.AppId); // ADR-061

            e.Property(x => x.AllowedRegions)
                .HasConversion(JsonValueConverter.For<List<string>>())
                .Metadata.SetValueComparer(JsonValueConverter.ListComparer<List<string>>());
        });

        modelBuilder.Entity<ViewDefinition>(e =>
        {
            e.HasKey(x => new { x.EntityType, x.Version, x.ViewKind }); // ADR-039 -- docs/data/dbcontext-and-conventions.md's own key

            e.Property(x => x.TemplateContent).IsRequired(); // portable TEXT/nvarchar(max) -- raw HTML+JS, never a native JSON column type

            e.Property(x => x.CompatibleSchemaVersions)
                .HasConversion(JsonValueConverter.For<List<int>>())
                .Metadata.SetValueComparer(JsonValueConverter.ListComparer<List<int>>());
        });

        modelBuilder.Entity<AccessLogEntry>(e =>
        {
            e.HasKey(x => x.SequenceNumber);
            e.HasIndex(x => x.ReaderActorId); // ADR-045 -- "every read by this reader" lookups
        });

        modelBuilder.Entity<EntityErasureKey>(e =>
        {
            e.HasKey(x => x.EntityId);
        });

        modelBuilder.Entity<LocalErasureKeyMaterial>(e =>
        {
            e.HasKey(x => x.KeyReference);
        });

        modelBuilder.Entity<FeatureFlagState>(e =>
        {
            e.HasKey(x => new { x.AppId, x.Key }); // ADR-077
        });

        modelBuilder.Entity<LeaderLease>(e =>
        {
            e.HasKey(x => x.WorkerRole); // ADR-078 -- deployment-wide, not AppId-scoped
        });

        modelBuilder.Entity<WebhookSubscription>(e =>
        {
            e.HasKey(x => x.SubscriptionId); // ADR-060

            e.Property(x => x.EventTypes)
                .HasConversion(JsonValueConverter.For<List<string>>())
                .Metadata.SetValueComparer(JsonValueConverter.ListComparer<List<string>>());
        });

        modelBuilder.Entity<WebhookOutbox>(e =>
        {
            e.HasKey(x => x.SequenceNumber);
            e.HasIndex(x => x.SubscriptionId); // WebhookOutboxPump's own "past this subscription's cursor" query
        });

        modelBuilder.Entity<WebhookDeliveryCursor>(e =>
        {
            e.HasKey(x => x.SubscriptionId);
        });
    }

    // docs/06-solution-structure.md, "Per-provider translation... Registration in
    // OnModelCreating -- unchanged": the marker method is never actually invoked;
    // HasTranslation substitutes the active provider's own SqlExpression for every
    // call site, built by the injected IJsonPathTranslator ("Follow API + Filter
    // Pushdown"'s own real implementation, not item 1's placeholder stub).
    private void RegisterJsonPathFunction(ModelBuilder modelBuilder, Delegate markerMethod, FilterableFieldType type)
    {
        modelBuilder.HasDbFunction(markerMethod.Method)
            .HasTranslation(args => jsonPathTranslator.Translate(args[0], (string)((SqlConstantExpression)args[1]).Value!, type));
    }
}
