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
            e.HasIndex(x => x.ShardKey); // queued sharding ADR
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
