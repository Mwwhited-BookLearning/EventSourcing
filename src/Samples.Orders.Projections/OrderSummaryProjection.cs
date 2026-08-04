using System.Text.Json.Nodes;
using EventStore.Projections.Abstractions;

namespace Samples.Orders.Projections;

// docs/features/cqrs-projections.md's worked example, verbatim -- no merge
// logic, no ChangeKind branch, no knowledge of which event just arrived.
public class OrderSummaryProjection : IProjection<OrderSummary>
{
    public string Name => "order-summary";

    public IReadOnlyCollection<string> EventTypes { get; } =
        ["OrderPlaced", "OrderAddressUpdated", "OrderShipped", "OrderCancelled"];

    public string GetKey(string eventType, JsonNode payload) => payload["OrderId"]!.GetValue<string>();

    public OrderSummary Project(string key, JsonNode mergedState) => new()
    {
        OrderId = key,
        CustomerName = mergedState["CustomerName"]?.GetValue<string>(),
        Address = mergedState["Address"]?.GetValue<string>(),
        Amount = mergedState["Amount"]?.GetValue<decimal>(),
        ShippedAt = mergedState["ShippedAt"]?.GetValue<DateTimeOffset?>(),
        CancelledAt = mergedState["CancelledAt"]?.GetValue<DateTimeOffset?>(),
    };
}
