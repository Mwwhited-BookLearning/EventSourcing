namespace Samples.Orders.Projections;

// Shape per docs/features/cqrs-projections.md's worked example, verbatim.
public class OrderSummary
{
    public string OrderId { get; set; } = default!;
    public string? CustomerName { get; set; }
    public string? Address { get; set; }
    public decimal? Amount { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}
