using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Persistence;

public class EventStoreContext(DbContextOptions<EventStoreContext> options) : DbContext(options)
{
    public DbSet<EventTypeDefinition> EventTypeDefinitions => Set<EventTypeDefinition>();
    public DbSet<FilterableField> FilterableFields => Set<FilterableField>();
    public DbSet<StoredEvent> Events => Set<StoredEvent>();
    public DbSet<EventParent> EventParents => Set<EventParent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventTypeDefinition>(e =>
        {
            e.HasKey(x => new { x.AppId, x.Name, x.Version });

            e.Property(x => x.JsonSchema).IsRequired(); // portable TEXT/nvarchar(max) -- never a native JSON column type (ADR-004)

            e.Property(x => x.RequiredClaims)
                .HasConversion(JsonValueConverter.For<List<RequiredClaim>>())
                .Metadata.SetValueComparer(JsonValueConverter.ListComparer<List<RequiredClaim>>());

            e.Property(x => x.RequiredSignature)
                .HasConversion(JsonValueConverter.ForNullable<RequiredSignature>());

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

            e.Property(x => x.Payload).IsRequired(); // portable TEXT/nvarchar(max)/text -- never a native JSON column type (ADR-004)

            e.Property(x => x.Signature)
                .HasConversion(JsonValueConverter.ForNullable<Signature>());
        });

        modelBuilder.Entity<EventParent>(e =>
        {
            // Soft references, deliberately no FK constraint (ADR-005) -- a Permissive
            // event type may name a ParentEventId that doesn't resolve to any StoredEvent yet.
            e.HasKey(x => new { x.ChildEventId, x.ParentEventId });
            e.HasIndex(x => x.ChildEventId);
            e.HasIndex(x => x.ParentEventId);
        });
    }
}
