using System.ComponentModel;
using System.Text.Json.Serialization;
using Hlibz.McpDocs.AspNetCore;
using ModelContextProtocol.Server;

namespace Sample.McpServer.Tools;

/// <summary>Recursive type → exercises $ref / $defs in the generated schema.</summary>
public record Category(
    [property: Description("Category id")] string Id,
    [property: Description("Display name")] string Name,
    [property: Description("Child categories (recursive)")] IReadOnlyList<Category> Children);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PercentDiscount), "percent")]
[JsonDerivedType(typeof(FixedDiscount), "fixed")]
public abstract record Discount;
public record PercentDiscount([property: Description("Percent off, 1..90")] int Percent) : Discount;
public record FixedDiscount([property: Description("Amount off")] decimal Amount, [property: Description("ISO 4217")] string Currency) : Discount;

[McpServerToolType]
[McpDocsGroup("Catalog")]
public class CatalogTools
{
    [McpServerTool(Name = "get_category_tree", Title = "Get category tree", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Returns the full category tree. The output type is recursive.")]
    public Category GetCategoryTree() =>
        new("root", "All", [new("mugs", "Mugs", []), new("tea", "Tea", [new("green", "Green tea", [])])]);

    [McpServerTool(Name = "update_prices", Title = "Bulk update prices", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Sets prices for many SKUs at once.\n\n- Keys are SKUs\n- Values are prices in `EUR`")]
    public string UpdatePrices([Description("SKU → new price")] Dictionary<string, decimal> prices) =>
        $"Updated {prices.Count} price(s).";

    [McpServerTool(Name = "apply_discount", Title = "Apply discount", Destructive = false, OpenWorld = false)]
    [Description("Applies either a percentage or a fixed discount to a product. Polymorphic parameter → `anyOf` in the schema.")]
    public string ApplyDiscount([Description("Product SKU")] string sku, [Description("Discount to apply")] Discount discount) =>
        $"Applied {discount} to {sku}.";
}
