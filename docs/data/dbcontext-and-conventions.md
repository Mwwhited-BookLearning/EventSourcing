[← Data model index](../02-data-model.md)

# DbContext & Cross-Cutting Conventions

Wiring and portability rules that apply **across** every entity group in
`schema-registry.md`, `event-log.md`, and `entity-store.md` — kept
separate from them so a convention that applies to all three isn't
duplicated three times or accidentally only stated for one.

```csharp
public class EventStoreContext : DbContext
{
    public EventStoreContext(DbContextOptions<EventStoreContext> options) : base(options) { }

    public DbSet<EventTypeDefinition> EventTypes => Set<EventTypeDefinition>();
    public DbSet<FilterableField> FilterableFields => Set<FilterableField>();
    public DbSet<StoredEvent> Events => Set<StoredEvent>();
    public DbSet<EventParent> EventParents => Set<EventParent>();
    public DbSet<EntityStoreRow> EntityStore => Set<EntityStoreRow>(); // ADR-021
    public DbSet<LiveEntityStoreRow> LiveEntityStore => Set<LiveEntityStoreRow>(); // ADR-042
    public DbSet<EntityErasureKey> EntityErasureKeys => Set<EntityErasureKey>(); // ADR-057
    public DbSet<ChainCheckpoint> ChainCheckpoints => Set<ChainCheckpoint>(); // ADR-089
    public DbSet<AppTrustRoot> AppTrustRoots => Set<AppTrustRoot>(); // ADR-044
    public DbSet<Role> Roles => Set<Role>(); // ADR-046/067
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>(); // ADR-046/067
    public DbSet<TrustedFederationIssuer> TrustedFederationIssuers => Set<TrustedFederationIssuer>(); // ADR-047
    public DbSet<AppDataResidencyPolicy> AppDataResidencyPolicies => Set<AppDataResidencyPolicy>(); // ADR-061
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>(); // ADR-060
    public DbSet<WebhookOutbox> WebhookOutboxItems => Set<WebhookOutbox>(); // ADR-060
    public DbSet<WebhookDeliveryCursor> WebhookDeliveryCursors => Set<WebhookDeliveryCursor>(); // ADR-060
    public DbSet<PeerSyncCursor> PeerSyncCursors => Set<PeerSyncCursor>(); // ADR-033
    public DbSet<ViewDefinition> ViewDefinitions => Set<ViewDefinition>(); // ADR-039
    public DbSet<FeatureFlagState> FeatureFlags => Set<FeatureFlagState>(); // ADR-077
    public DbSet<LeaderLease> LeaderLeases => Set<LeaderLease>(); // ADR-078

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventTypeDefinition>(e =>
        {
            e.HasKey(x => new { x.AppId, x.Name, x.Version }); // ADR-030 — AppId joins the key
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.FilterableFields)
             .WithOne()
             .HasForeignKey(f => new { f.EventTypeName, f.EventTypeVersion });
        });

        modelBuilder.Entity<FilterableField>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.JsonPath).HasMaxLength(500);
        });

        modelBuilder.Entity<StoredEvent>(e =>
        {
            e.HasKey(x => x.SequenceNumber);
            e.Property(x => x.SequenceNumber).ValueGeneratedOnAdd();
            e.HasIndex(x => x.EventType);
            e.HasIndex(x => x.OccurredAt); // ADR-029 — fold ordering reads this, not just SequenceNumber
            e.HasIndex(x => x.EntityId); // ADR-021 — supports entity change-history queries (ADR-024)
            e.HasIndex(x => x.EventId).IsUnique(); // FK target for EventParents; also blocks duplicate EventId publishes
            e.Property(x => x.Payload).HasColumnType("TEXT"); // portable; see Portability rules below
        });

        modelBuilder.Entity<EntityStoreRow>(e =>
        {
            e.HasKey(x => x.EntityId);
            e.HasIndex(x => x.EntityType);
            e.HasIndex(x => x.ShardKey); // ADR-034
            e.Property(x => x.Data).HasColumnType("TEXT");
            e.Property(x => x.Extensions).HasColumnType("TEXT");
        });

        modelBuilder.Entity<EventParent>(e =>
        {
            e.HasKey(x => new { x.ChildEventId, x.ParentEventId });
            e.HasIndex(x => x.ParentEventId); // supports descendant traversal (find children of X)
            // No database-level FK on ParentEventId -> Events.EventId: Permissive event types must be able
            // to insert a dangling reference, which a real FK constraint would reject outright. Strict-mode
            // existence checking is therefore enforced in the application layer at publish time, not the schema.
        });

        // The following were named in ADRs but missing from this DbContext until this pass
        // (this project's data-model drift table, tracked in TODO.md) — added together here.

        modelBuilder.Entity<LiveEntityStoreRow>(e =>
        {
            e.HasKey(x => x.EntityId); // ADR-042 -- folds every event immediately, no AuthorityStatus gate
            e.HasIndex(x => x.EntityType);
            e.Property(x => x.Data).HasColumnType("TEXT");
            e.Property(x => x.Extensions).HasColumnType("TEXT");
        });

        modelBuilder.Entity<EntityErasureKey>(e =>
        {
            e.HasKey(x => x.EntityId); // ADR-057 -- same key as EntityStoreRow, one-to-one
        });

        modelBuilder.Entity<ChainCheckpoint>(e =>
        {
            e.HasKey(x => x.SequenceNumberRangeEnd); // ADR-089 -- a checkpoint is looked up by "what's the latest archived boundary"
        });

        modelBuilder.Entity<AppTrustRoot>(e =>
        {
            e.HasKey(x => new { x.AppId, x.IssuerDid }); // ADR-030/044
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.HasKey(x => new { x.AppId, x.RoleName }); // ADR-030/046
        });

        modelBuilder.Entity<UserPermission>(e =>
        {
            e.HasKey(x => new { x.ActorId, x.AppId, x.Permission }); // ADR-046/067 -- additive-only, no explicit-deny row
        });

        modelBuilder.Entity<TrustedFederationIssuer>(e =>
        {
            e.HasKey(x => new { x.AppId, x.Issuer }); // ADR-030/047
        });

        modelBuilder.Entity<AppDataResidencyPolicy>(e =>
        {
            e.HasKey(x => x.AppId); // ADR-061 -- absent for a given AppId means unconstrained
        });

        modelBuilder.Entity<WebhookSubscription>(e =>
        {
            e.HasKey(x => x.SubscriptionId); // ADR-060
            e.HasIndex(x => x.AppId);
        });

        modelBuilder.Entity<WebhookOutbox>(e =>
        {
            e.HasKey(x => x.SequenceNumber); // ADR-060
            e.HasIndex(x => x.SubscriptionId);
        });

        modelBuilder.Entity<WebhookDeliveryCursor>(e =>
        {
            e.HasKey(x => x.SubscriptionId); // ADR-060 -- one cursor per subscription
        });

        modelBuilder.Entity<PeerSyncCursor>(e =>
        {
            e.HasKey(x => x.PeerId); // ADR-033
        });

        modelBuilder.Entity<ViewDefinition>(e =>
        {
            e.HasKey(x => new { x.EntityType, x.Version, x.ViewKind }); // ADR-039
        });

        modelBuilder.Entity<FeatureFlagState>(e =>
        {
            e.HasKey(x => new { x.AppId, x.Key }); // ADR-077
        });

        modelBuilder.Entity<LeaderLease>(e =>
        {
            e.HasKey(x => x.WorkerRole); // ADR-078 -- deployment-wide, not AppId-scoped
        });
    }
}
```

## Portability rules (apply to all providers)

1. **No native JSON column types in the shared model.** `Payload` and
   `JsonSchema` are plain text columns (`TEXT` / `nvarchar(max)` /
   `text`), never SQL Server's `json` type or Postgres's `jsonb` column
   type at the EF model level. Native JSON *functions* are still used for
   querying (see `../04-odata-filter-pushdown.md`) — that's a query-time
   concern, not a column-type concern, and keeps `dotnet ef migrations`
   generating a consistent model across providers.
2. **Auto-increment key** (`SequenceNumber`) uses `ValueGeneratedOnAdd()` —
   this maps to `INTEGER PRIMARY KEY` (SQLite rowid), a Postgres identity
   sequence, and SQL Server `IDENTITY` without extra configuration.
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
