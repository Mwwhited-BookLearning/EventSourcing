using Microsoft.EntityFrameworkCore;

namespace EventStore.Projections.Host;

// A separate database from EventStoreContext, per docs/09-cqrs-read-models.md
// -- never the same connection string, never a cross-database join. Abstract
// so a worked example's own DbContext (e.g. Samples.Orders.Projections'
// OrdersProjectionsDbContext) can add its own read-model DbSet(s) while
// ProjectionHost<TReadModel>'s own generic mechanics -- which never need a
// concrete DbSet property, only DbContext.Set<TReadModel>() -- stay reusable
// across any projection built on this host.
public abstract class ProjectionsDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<ProjectionCheckpoint> Checkpoints => Set<ProjectionCheckpoint>();
    public DbSet<ProjectionSnapshot> Snapshots => Set<ProjectionSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectionCheckpoint>(e => e.HasKey(x => x.ProjectionName));
        modelBuilder.Entity<ProjectionSnapshot>(e => e.HasKey(x => new { x.ProjectionName, x.Key }));
    }
}
