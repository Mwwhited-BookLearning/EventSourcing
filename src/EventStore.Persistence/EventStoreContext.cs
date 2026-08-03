using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
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
