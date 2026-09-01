using EventStore.Projections.Host;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Flows;

// Framework-generic (unlike Samples.Orders.Projections' own OrdersProjectionsDbContext) --
// PendingTask's shape is fixed across every flow/domain, so it lives here in
// EventStore.Flows itself rather than in a Samples.* project.
public class PendingTasksDbContext(DbContextOptions<PendingTasksDbContext> options) : ProjectionsDbContext(options)
{
    public DbSet<PendingTask> PendingTasks => Set<PendingTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<PendingTask>(e => e.HasKey(x => x.Key));
    }
}
