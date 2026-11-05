using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Sample.McpServer.Resources;

[McpServerResourceType]
public static class CatalogResources
{
    [McpServerResource(UriTemplate = "catalog://products/{sku}", Name = "product", Title = "Product by SKU", MimeType = "application/json")]
    [Description("A product from the catalog, addressed by SKU.")]
    public static string Product(string sku) => $$"""{"sku":"{{sku}}","name":"Red mug","price":12.5}""";

    [McpServerResource(UriTemplate = "catalog://readme", Name = "catalog_readme", Title = "Catalog README", MimeType = "text/markdown")]
    [Description("Static description of the catalog.")]
    public static string Readme() => "# Catalog\nAll products sold by Acme.";

    [McpServerResource(UriTemplate = "catalog://internal/costs", Name = "unit_costs", Title = "Unit costs", MimeType = "text/csv")]
    [Description("Purchase cost per SKU. Finance only.")]
    [Authorize(Policy = "Finance")]
    public static string UnitCosts() => "sku,cost\nSKU-RED-MUG,4.10";
}
