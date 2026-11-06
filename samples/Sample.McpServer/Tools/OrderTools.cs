using System.ComponentModel;
using Hlibz.McpDocs.AspNetCore;
using ModelContextProtocol;
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

/// <summary>
/// One glyph per order tool, all on the same indigo tile, so the sidebar reads as a set.
/// SVG data URIs, URL-encoded (<c>%3C</c> = &lt;, <c>%3E</c> = &gt;, <c>%23</c> = #).
/// </summary>
internal static class OrderIcons
{
    private const string Open = "data:image/svg+xml;utf8,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'%3E" +
        "%3Crect width='16' height='16' rx='3' fill='%234f46e5'/%3E" +
        "%3Cg fill='none' stroke='white' stroke-width='1.6' stroke-linecap='round' stroke-linejoin='round'%3E";
    private const string Close = "%3C/g%3E%3C/svg%3E";

    public const string Get = Open + "%3Cpath d='M5 3.5h4l2 2v7H5z'/%3E%3Cpath d='M7 8.5h2M7 10.5h2'/%3E" + Close;          // document
    public const string Search = Open + "%3Ccircle cx='7' cy='7' r='3'/%3E%3Cpath d='M9.3 9.3L12 12'/%3E" + Close;          // magnifier
    public const string Count = Open + "%3Cpath d='M6.5 3.5l-1 9M10.5 3.5l-1 9M4 6.5h8.5M3.5 9.5h8.5'/%3E" + Close;           // hash
    public const string Create = Open + "%3Cpath d='M4.5 8h7M8 4.5v7'/%3E" + Close;                                             // plus
    public const string Cancel = Open + "%3Cpath d='M5 5l6 6M11 5l-6 6'/%3E" + Close;                                           // cross
}

/// <summary>Orders: read-only lookups and mutating operations with mixed annotations.</summary>
[McpServerToolType]
[McpDocsGroup("Orders")]
public class OrderTools
{
    [McpServerTool(IconSource = OrderIcons.Get, Name = "get_order", Title = "Get order", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Returns a single order by its id. Output is structured (has an output schema).\n\n" +
                 "Unknown ids fail with `isError: true` and a text message (try `ORD-404`). MCP has no schema for errors.")]
    public Order GetOrder([Description("Order id, e.g. 'ORD-1001'")] string orderId) =>
        orderId.StartsWith("ORD-", StringComparison.Ordinal) && orderId != "ORD-404"
            ? new(orderId, OrderStatus.Paid, 42.50m, "EUR", DateTimeOffset.UtcNow.AddDays(-2),
                [new OrderLine("SKU-RED-MUG", 2), new OrderLine("SKU-TEA-100G", 1)])
            // McpException → a tool result with isError: true; its message reaches the client as text content.
            : throw new McpException($"Order '{orderId}' not found.");

    [McpServerTool(IconSource = OrderIcons.Search, Name = "search_orders", Title = "Search orders", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Searches orders by status and date range. Supports paging.\n\n" +
                 "Returns a list. Protocol 2026-07-28 sends the array as is; older protocols wrap it as `{ \"result\": [...] }`.")]
    public IReadOnlyList<Order> SearchOrders(
        [Description("Filter by status")] OrderStatus? status = null,
        [Description("Created at or after (ISO 8601)")] DateTimeOffset? from = null,
        [Description("Created before (ISO 8601)")] DateTimeOffset? to = null,
        [Description("Page size, 1..100")] int pageSize = 20,
        [Description("Opaque cursor from a previous page")] string? cursor = null) =>
        [new("ORD-1001", status ?? OrderStatus.Paid, 42.50m, "EUR", from ?? DateTimeOffset.UtcNow.AddDays(-2), [new OrderLine("SKU-RED-MUG", 2)])];

    [McpServerTool(IconSource = OrderIcons.Count, Name = "count_orders", Title = "Count orders", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Counts orders with the given status. Returns a bare number (wrapped as `{ \"result\": 3 }` on protocol 2025-11-25 and older).")]
    public int CountOrders([Description("Filter by status")] OrderStatus? status = null) => status is null ? 12 : 3;

    [McpServerTool(IconSource = OrderIcons.Create, Name = "create_order", Title = "Create order", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Creates a new order with a nested shipping address and a list of lines. Returns the created order; " +
                 "`OrderLine` appears in both the parameters and the result.")]
    public Order CreateOrder(
        [Description("Customer id")] string customerId,
        [Description("Shipping address")] Address shippingAddress,
        [Description("Order lines, at least one")] OrderLine[] lines,
        [Description("Free-form note for the warehouse")] string? note = null) =>
        lines.Length == 0
            ? throw new McpException("An order needs at least one line.")
            : new($"ORD-{Random.Shared.Next(1000, 9999)}", OrderStatus.Pending, 0m, "EUR", DateTimeOffset.UtcNow, lines);

    [McpServerTool(IconSource = OrderIcons.Cancel, Name = "cancel_order", Title = "Cancel order", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Cancels an order. **Destructive**: refunds are issued and the order cannot be reopened.")]
    public string CancelOrder(
        [Description("Order id")] string orderId,
        [Description("Reason shown to the customer")] string reason) =>
        $"Order {orderId} cancelled: {reason}";
}
