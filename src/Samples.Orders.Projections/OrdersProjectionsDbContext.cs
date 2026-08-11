using EventStore.Projections.Host;
using Microsoft.EntityFrameworkCore;

namespace Samples.Orders.Projections;

public class OrdersProjectionsDbContext(DbContextOptions<OrdersProjectionsDbContext> options) : ProjectionsDbContext(options)
{
    public DbSet<OrderSummary> OrderSummaries => Set<OrderSummary>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OrderSummary>(e => e.HasKey(x => x.OrderId));
    }
}
