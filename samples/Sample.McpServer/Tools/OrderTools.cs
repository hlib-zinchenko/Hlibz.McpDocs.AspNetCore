using System.ComponentModel;
using Hlibz.McpDocs.AspNetCore;
using ModelContextProtocol.Server;

namespace Sample.McpServer.Tools;

public enum OrderStatus { Pending, Paid, Shipped, Cancelled }

public record Address(
    [property: Description("Street and house number")] string Street,
    [property: Description("City name")] string City,
    [property: Description("ISO 3166-1 alpha-2 country code, e.g. 'UA'")] string Country,
    [property: Description("Postal code, optional for some countries")] string? PostalCode = null);

public record OrderLine(
    [property: Description("Product SKU")] string Sku,
    [property: Description("Quantity, 1..100")] int Quantity);

public record Order(string Id, OrderStatus Status, decimal Total, string Currency, DateTimeOffset CreatedAt, IReadOnlyList<OrderLine> Lines);

/// <summary>Orders: read-only lookups and mutating operations with mixed annotations.</summary>
[McpServerToolType]
[McpDocsGroup("Orders")]
public class OrderTools
{
    [McpServerTool(Name = "get_order", Title = "Get order", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Returns a single order by its id. Output is structured (has an output schema).")]
    public Order GetOrder([Description("Order id, e.g. 'ORD-1001'")] string orderId) =>
        new(orderId, OrderStatus.Paid, 42.50m, "EUR", DateTimeOffset.UtcNow.AddDays(-2),
            [new OrderLine("SKU-RED-MUG", 2), new OrderLine("SKU-TEA-100G", 1)]);

    [McpServerTool(Name = "search_orders", Title = "Search orders", ReadOnly = true, Idempotent = true)]
    [Description("Searches orders by status and date range. Supports paging.")]
    public string SearchOrders(
        [Description("Filter by status")] OrderStatus? status = null,
        [Description("Created at or after (ISO 8601)")] DateTimeOffset? from = null,
        [Description("Created before (ISO 8601)")] DateTimeOffset? to = null,
        [Description("Page size, 1..100")] int pageSize = 20,
        [Description("Opaque cursor from a previous page")] string? cursor = null) =>
        $"Found 0 orders (status={status}, from={from:o}, to={to:o}, pageSize={pageSize}, cursor={cursor})";

    [McpServerTool(Name = "create_order", Title = "Create order", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a new order with a nested shipping address and a list of lines.")]
    public string CreateOrder(
        [Description("Customer id")] string customerId,
        [Description("Shipping address")] Address shippingAddress,
        [Description("Order lines, at least one")] OrderLine[] lines,
        [Description("Free-form note for the warehouse")] string? note = null) =>
        $"Created order ORD-{Random.Shared.Next(1000, 9999)} for {customerId} with {lines.Length} line(s) to {shippingAddress.City}.";

    [McpServerTool(Name = "cancel_order", Title = "Cancel order", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Cancels an order. **Destructive**: refunds are issued and the order cannot be reopened.")]
    public string CancelOrder(
        [Description("Order id")] string orderId,
        [Description("Reason shown to the customer")] string reason) =>
        $"Order {orderId} cancelled: {reason}";
}
