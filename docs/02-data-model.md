# Data Model

## Entities

```csharp
public class EventTypeDefinition
{
    public string Name { get; set; } = default!;      // e.g. "OrderPlaced" — canonical casing, stored lowercase for lookup
    public int Version { get; set; }
    public string JsonSchema { get; set; } = default!; // raw JSON Schema document, stored as text
    public DateTimeOffset RegisteredAt { get; set; }
    public bool IsActive { get; set; }                 // latest version flag

    public List<FilterableField> FilterableFields { get; set; } = new();
}

public class FilterableField
{
    public int Id { get; set; }
    public string EventTypeName { get; set; } = default!;
    public int EventTypeVersion { get; set; }
    public string JsonPath { get; set; } = default!;    // e.g. "$.Amount"
    public FilterableFieldType DataType { get; set; }   // String, Number, Boolean, DateTimeOffset
    public bool IsIndexed { get; set; }                 // whether a DB index/computed column exists
}

public enum FilterableFieldType { String, Number, Boolean, DateTimeOffset }

public class StoredEvent
{
    public long SequenceNumber { get; set; }   // global monotonic order, identity column
    public Guid EventId { get; set; }
    public string EventType { get; set; } = default!;  // normalized lowercase
    public int SchemaVersion { get; set; }
    public string? StreamId { get; set; }              // optional aggregate/stream key
    public string Payload { get; set; } = default!;    // JSON text, validated at publish time
    public DateTimeOffset OccurredAt { get; set; }
}
```

## DbContext

```csharp
public class EventStoreContext : DbContext
{
    public EventStoreContext(DbContextOptions<EventStoreContext> options) : base(options) { }

    public DbSet<EventTypeDefinition> EventTypes => Set<EventTypeDefinition>();
    public DbSet<FilterableField> FilterableFields => Set<FilterableField>();
    public DbSet<StoredEvent> Events => Set<StoredEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventTypeDefinition>(e =>
        {
            e.HasKey(x => new { x.Name, x.Version });
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
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.StreamId);
            e.Property(x => x.Payload).HasColumnType("TEXT"); // portable; see provider notes below
        });
    }
}
```

## Portability rules (apply to all providers)

1. **No native JSON column types in the shared model.** `Payload` and
   `JsonSchema` are plain text columns (`TEXT` / `nvarchar(max)` /
   `text`), never SQL Server's `json` type or Postgres's `jsonb` column
   type at the EF model level. Native JSON *functions* are still used for
   querying (see `04-odata-filter-pushdown.md`) — that's a query-time
   concern, not a column-type concern, and keeps `dotnet ef migrations`
   generating a consistent model across providers.
2. **Auto-increment key** (`SequenceNumber`) uses `ValueGeneratedOnAdd()` —
   this maps to `INTEGER PRIMARY KEY` (SQLite rowid), a Postgres identity
   sequence, and SQL Server `IDENTITY` without extra configuration.
3. **No `rowversion`/native optimistic concurrency tokens.** If optimistic
   concurrency is needed later, use a manual `int RowVersion` column
   incremented in application code — `rowversion` (SQL Server) has no
   equivalent in SQLite/Postgres.
4. **Casing**: SQL Server's default collation is case-insensitive; SQLite
   and Postgres are case-sensitive by default. `EventType` and
   `EventTypeDefinition.Name` are always normalized to lowercase before
   storage and before querying, so string equality behaves identically
   across all three providers regardless of collation.
5. **Per-provider migrations**: EF Core migrations are not portable across
   providers even with an identical model (different SQL emitted). Keep
   one migrations assembly/folder per provider — see
   `06-solution-structure.md`.

## Per-provider index strategy for filterable fields

When a `FilterableField` is marked `IsIndexed = true`, the registry service
issues a provider-specific migration to add a computed/expression index:

| Provider | Mechanism |
|---|---|
| SQLite | Expression index: `CREATE INDEX ... ON Events(json_extract(Payload, '$.Amount'))` (SQLite 3.9+) |
| PostgreSQL | Expression index: `CREATE INDEX ... ON "Events" ((("Payload"::jsonb) ->> 'Amount'))` |
| SQL Server | Computed column + index: `ALTER TABLE Events ADD Amount AS JSON_VALUE(Payload, '$.Amount'); CREATE INDEX ... ON Events(Amount)` |

This is generated/applied by the Schema Registry Service at field-registration
time, not part of the baseline EF model — see
`05-schema-registry-and-spec-generation.md`.
