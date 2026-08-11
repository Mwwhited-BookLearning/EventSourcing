[← Data model index](../02-data-model.md)

# DbContext & Cross-Cutting Conventions

Wiring and portability rules that apply **across** every entity group in
`schema-registry.md`, `event-log.md`, and `entity-store.md` — kept
separate from them so a convention that applies to all three isn't
duplicated three times or accidentally only stated for one.

```csharp
// IJsonPathTranslator is a required constructor dependency (not just registered in
// DI for endpoints to resolve) because OnModelCreating needs a concrete translator
// instance to close over when registering the four JsonFunctions DbFunctions used
// by ../04-odata-filter-pushdown.md's pushdown mechanism. ASP.NET Core's
// AddDbContext resolves this automatically; anything constructing this context
// directly (every EventStore.IntegrationTests provider test class) must pass the
// matching provider's translator explicitly.
public class EventStoreContext(DbContextOptions<EventStoreContext> options, IJsonPathTranslator jsonPathTranslator) : DbContext(options)
{
    public DbSet<EventTypeDefinition> EventTypeDefinitions => Set<EventTypeDefinition>();
    public DbSet<FilterableField> FilterableFields => Set<FilterableField>();
    public DbSet<StoredEvent> Events => Set<StoredEvent>();
    public DbSet<EventParent> EventParents => Set<EventParent>();
    public DbSet<DerivationDefinition> DerivationDefinitions => Set<DerivationDefinition>(); // ADR-007
    public DbSet<DerivationCursor> DerivationCursors => Set<DerivationCursor>(); // ADR-007
    public DbSet<PendingJoinState> PendingJoinStates => Set<PendingJoinState>(); // ADR-007
    public DbSet<EntityStoreRow> EntityStore => Set<EntityStoreRow>(); // ADR-021
    public DbSet<LiveEntityStoreRow> LiveEntityStore => Set<LiveEntityStoreRow>(); // ADR-042
    public DbSet<TelemetryChannel> TelemetryChannels => Set<TelemetryChannel>(); // ADR-031/081
    public DbSet<TelemetrySample> TelemetrySamples => Set<TelemetrySample>(); // ADR-031
    public DbSet<RedactedRange> RedactedRanges => Set<RedactedRange>(); // ADR-031
    public DbSet<Attachment> Attachments => Set<Attachment>(); // ADR-032
    public DbSet<AttachmentRef> AttachmentRefs => Set<AttachmentRef>(); // ADR-032
    public DbSet<PeerSyncCursor> PeerSyncCursors => Set<PeerSyncCursor>(); // ADR-033
    public DbSet<AppResidencyPolicy> AppResidencyPolicies => Set<AppResidencyPolicy>(); // ADR-061
    public DbSet<ViewDefinition> ViewDefinitions => Set<ViewDefinition>(); // ADR-039
    public DbSet<AccessLogEntry> AccessLogEntries => Set<AccessLogEntry>(); // ADR-045
    public DbSet<EntityErasureKey> EntityErasureKeys => Set<EntityErasureKey>(); // ADR-057
    public DbSet<LocalErasureKeyMaterial> LocalErasureKeyMaterials => Set<LocalErasureKeyMaterial>(); // ADR-057
    public DbSet<FeatureFlagState> FeatureFlags => Set<FeatureFlagState>(); // ADR-077
    public DbSet<LeaderLease> LeaderLeases => Set<LeaderLease>(); // ADR-078
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>(); // ADR-060
    public DbSet<WebhookOutbox> WebhookOutbox => Set<WebhookOutbox>(); // ADR-060
    public DbSet<WebhookDeliveryCursor> WebhookDeliveryCursors => Set<WebhookDeliveryCursor>(); // ADR-060

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Also registers the four JsonPath DbFunctions (../04-odata-filter-pushdown.md) here — omitted
        // from this sketch as query-pushdown plumbing, not part of the persisted shape.

        modelBuilder.Entity<EventTypeDefinition>(e =>
        {
            e.HasKey(x => new { x.AppId, x.Name, x.Version }); // ADR-030 — AppId joins the key
            e.Property(x => x.JsonSchema).IsRequired(); // portable TEXT/nvarchar(max) -- never a native JSON column type (ADR-004)
            e.HasMany(x => x.FilterableFields)
             .WithOne()
             .HasForeignKey(f => new { f.EventTypeAppId, f.EventTypeName, f.EventTypeVersion }) // three-part FK — AppId included, not just Name+Version
             .HasPrincipalKey(x => new { x.AppId, x.Name, x.Version });
        });

        modelBuilder.Entity<FilterableField>(e =>
        {
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<StoredEvent>(e =>
        {
            e.HasKey(x => x.SequenceNumber);
            e.HasIndex(x => x.EventId).IsUnique(); // ADR-011 — publish idempotency relies on this constraint existing
            e.HasIndex(x => x.EntityId); // ADR-021 — QUERY /entities/{entityId}/events and the fold step's own per-entity lookups
            e.Property(x => x.Payload).IsRequired(); // portable TEXT/nvarchar(max)/text -- never a native JSON column type (ADR-004)
        });

        modelBuilder.Entity<EventParent>(e =>
        {
            // Soft references, deliberately no FK constraint (ADR-005) -- a Permissive
            // event type may name a ParentEventId that doesn't resolve to any StoredEvent yet.
            e.HasKey(x => new { x.ChildEventId, x.ParentEventId });
            e.HasIndex(x => x.ChildEventId);
            e.HasIndex(x => x.ParentEventId); // supports descendant traversal (find children of X)
        });

        modelBuilder.Entity<DerivationDefinition>(e =>
        {
            e.HasKey(x => new { x.AppId, x.Name }); // ADR-007
        });

        modelBuilder.Entity<DerivationCursor>(e =>
        {
            e.HasKey(x => new { x.AppId, x.DerivationName, x.SourceEventType }); // ADR-007
        });

        modelBuilder.Entity<PendingJoinState>(e =>
        {
            e.HasKey(x => x.Id);
            // Not unique: "at most one ACTIVE (ExpiredReason IS NULL) row per key" is an
            // application-level invariant, not a database constraint.
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
            e.HasKey(x => x.EntityId); // ADR-042 -- folds every event immediately, no AuthorityStatus gate
            e.HasIndex(x => x.EntityType);
        });

        modelBuilder.Entity<TelemetryChannel>(e =>
        {
            e.HasKey(x => x.ChannelId);
            e.HasIndex(x => x.ThreadId); // ADR-081 -- the ThreadId-scoped grouped session read
        });

        modelBuilder.Entity<TelemetrySample>(e =>
        {
            e.HasKey(x => new { x.ChannelId, x.Timestamp });
        });

        modelBuilder.Entity<RedactedRange>(e =>
        {
            e.HasKey(x => new { x.ChannelId, x.FromTimestamp });
        });

        modelBuilder.Entity<Attachment>(e =>
        {
            e.HasKey(x => x.ContentHash);
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
            e.HasKey(x => x.PeerId); // ADR-033
        });

        modelBuilder.Entity<AppResidencyPolicy>(e =>
        {
            e.HasKey(x => x.AppId); // ADR-061 -- absent for a given AppId means unconstrained
        });

        modelBuilder.Entity<ViewDefinition>(e =>
        {
            e.HasKey(x => new { x.EntityType, x.Version, x.ViewKind }); // ADR-039
        });

        modelBuilder.Entity<AccessLogEntry>(e =>
        {
            e.HasKey(x => x.SequenceNumber);
            e.HasIndex(x => x.ReaderActorId); // ADR-045 -- "every read by this reader" lookups
        });

        modelBuilder.Entity<EntityErasureKey>(e =>
        {
            e.HasKey(x => x.EntityId); // ADR-057 -- same key as EntityStoreRow, one-to-one
        });

        modelBuilder.Entity<LocalErasureKeyMaterial>(e =>
        {
            e.HasKey(x => x.KeyReference); // ADR-057
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
        });

        modelBuilder.Entity<WebhookOutbox>(e =>
        {
            e.HasKey(x => x.SequenceNumber); // ADR-060
            e.HasIndex(x => x.SubscriptionId); // WebhookOutboxPump's own "past this subscription's cursor" query
        });

        modelBuilder.Entity<WebhookDeliveryCursor>(e =>
        {
            e.HasKey(x => x.SubscriptionId); // ADR-060 -- one cursor per subscription
        });
    }
}
```

Every `List<T>`-typed and nullable-class-typed property shown above without an
explicit conversion (`EventTypeDefinition.RequiredClaims`/`RequiredSignature`,
`StoredEvent.Signature`, `DerivationDefinition.Sources`/`JoinConditions`/
`SelectFields`, `TelemetryChannel.SourceChannelIds`, `Attachment.ChunkIndex`,
`AppResidencyPolicy.AllowedRegions`, `ViewDefinition.CompatibleSchemaVersions`,
`WebhookSubscription.EventTypes`) is actually configured in
`EventStoreContext.OnModelCreating` with a `JsonValueConverter` (a
JSON-serializing `ValueConverter<T>`) plus a matching `ValueComparer` —
omitted from the sketch above since it's the same one-line pattern repeated
per property, not a distinct structural decision. See
`src/EventStore.Persistence/EventStoreContext.cs` for the literal code.

`AppTrustRoot`/`Role`/`RoleAssignment`/`UserPermission`/
`TrustedFederationIssuer`/`FederatedIdentityMapping` are **not** on
`EventStoreContext` at all — they live on `EventStore.DevIdp`'s own
`DevIdpDbContext` (`src/EventStore.DevIdp/DevIdpDbContext.cs`), a separate,
throwaway dev-mode-IdP EF Core InMemory store, deliberately never part of
the durable event log. See `schema-registry.md`'s own sections on these
entities for that ownership split.

`ADR-089`'s archival `ChainCheckpoint` (`{SequenceNumberRangeStart,
SequenceNumberRangeEnd, ChainHashAtRangeEnd, ContentProviderKey,
ContentProviderRef}`, documented in `event-log.md`) is not yet a registered
`DbSet`/migrated table on `EventStoreContext` — check `08-build-plan.md`'s
Implementation status table before assuming the archival mechanism it
describes is live.

## Portability rules (apply to all providers)

1. **No native JSON column types in the shared model.** `Payload` and
   `JsonSchema` are plain text columns (`TEXT` / `nvarchar(max)` /
   `text`), never SQL Server's `json` type or Postgres's `jsonb` column
   type at the EF model level. Native JSON *functions* are still used for
   querying (see `../04-odata-filter-pushdown.md`) — that's a query-time
   concern, not a column-type concern, and keeps `dotnet ef migrations`
   generating a consistent model across providers.
2. **Auto-increment key** (`SequenceNumber`, `AccessLogEntry.SequenceNumber`,
   `WebhookOutbox.SequenceNumber`) relies on EF Core's own convention for a
   numeric primary key — `ValueGeneratedOnAdd` by default, no explicit fluent
   call needed — which maps to `INTEGER PRIMARY KEY` (SQLite rowid), a
   Postgres identity sequence, and SQL Server `IDENTITY` without extra
   configuration.
3. **No `rowversion`/native optimistic concurrency tokens.** Optimistic
   concurrency (`ADR-024`) uses the plain `long Version` column on
   `EntityStoreRow`, incremented in application code — `rowversion` (SQL
   Server) has no equivalent in SQLite/Postgres.
4. **Casing**: SQL Server's default collation is case-insensitive; SQLite
   and Postgres are case-sensitive by default. `EventType` and
   `EventTypeDefinition.Name` are always normalized to lowercase before
   storage and before querying, so string equality behaves identically
   across all three providers regardless of collation.
5. **Per-provider migrations**: EF Core migrations are not portable across
   providers even with an identical model (different SQL emitted). Keep
   one migrations assembly/folder per provider — see
   `../06-solution-structure.md`. Each provider's migrations assembly is
   referenced directly by exactly one deployable (`EventStore.Host.<Provider>`,
   `ADR-001`) — there is no runtime selection between them.
