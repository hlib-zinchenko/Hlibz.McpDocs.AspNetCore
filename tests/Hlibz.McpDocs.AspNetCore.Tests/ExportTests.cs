using System.ComponentModel;
using System.Net;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Server;

using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

public class ExportTests
{
    [Fact]
    public async Task Markdown_lists_the_catalog_and_hides_what_the_viewer_may_not_see()
    {
        await using TestApp app = await TestApp.StartAsync();
        string anon = await app.Client().GetStringAsync("/mcp-docs/catalog.md", TestContext.Current.CancellationToken);
        string admin = await app.Client("admin").GetStringAsync("/mcp-docs/catalog.md", TestContext.Current.CancellationToken);

        Assert.StartsWith("# Test Server", anon);
        Assert.Contains("### get_thing", anon);
        Assert.Contains("| `id` | string | yes | The id |", anon);
        Assert.Contains("Annotations: read-only, open-world", anon);
        Assert.Contains("### greet", anon);
        Assert.Contains("URI template: `thing://{id}`", anon);
        Assert.Contains("Endpoint: http://localhost/mcp", anon);
        Assert.DoesNotContain("delete_thing", anon);
        Assert.Contains("### delete_thing", admin);
        Assert.Contains("Requires: role `admin`", admin);
    }

    [Fact]
    public async Task Markdown_describes_a_result_that_is_not_an_object()
    {
        await using TestApp app = await TestApp.StartAsync();
        string md = await app.Client().GetStringAsync("/mcp-docs/catalog.md", TestContext.Current.CancellationToken);

        Assert.Contains("Type: `array of object`. Clients on protocol 2025-11-25 or older receive it wrapped as `{ \"result\": ... }`.", md);
        Assert.Contains("Each item:", md);
        Assert.Contains("| `id` | string | yes | Thing id |", md);
    }

    public record Category([property: Description("Category id")] string Id, [property: Description("Sub-categories")] List<Category> Children, Category? Parent);
    public record Line([property: Description("Product SKU")] string Sku, int Quantity);
    public record Order(Line[] Lines, Dictionary<string, Line> ByWarehouse, [property: Description("Ship to")] Address? ShipTo);
    public record Address([property: Description("City name")] string City);

    [McpServerToolType]
    public sealed class NestedTools
    {
        [McpServerTool(Name = "save_category"), Description("Saves a category tree.")]
        public string SaveCategory([Description("The tree")] Category category) => category.Id;

        [McpServerTool(Name = "place_order"), Description("Places an order.")]
        public string PlaceOrder([Description("The order")] Order order) => "ok";
    }

    [Fact]
    public async Task Markdown_lists_nested_fields_and_stops_at_recursion()
    {
        await using TestApp app = await TestApp.StartAsync(server: s => s.WithTools<NestedTools>());
        string md = await app.Client().GetStringAsync("/mcp-docs/catalog.md", TestContext.Current.CancellationToken);
        string[] lines = md.Split('\n');
        string Row(string name) => Assert.Single(lines, l => l.StartsWith("| `" + name + "` |"));

        Assert.Contains("| object | yes | The tree |", Row("category"));
        Assert.Contains("| string | yes | Category id |", Row("category.id"));
        Assert.Contains("array of object (recursive: same as `category`)", Row("category.children"));
        Assert.DoesNotContain(lines, l => l.StartsWith("| `category.children[]"));
        // The SDK inlines the nullable parent once and refers back to it after that.
        Assert.Contains("Category id", Row("category.parent.id"));
        Assert.Contains("(recursive: same as `category.parent`)", Row("category.parent.parent"));
        Assert.DoesNotContain(lines, l => l.StartsWith("| `category.parent.parent."));

        Assert.Contains("| string | yes | Product SKU |", Row("order.lines[].sku"));
        Assert.Contains("integer", Row("order.lines[].quantity"));
        Assert.Contains("Product SKU", Row("order.byWarehouse.<key>.sku"));
        Assert.Contains("City name", Row("order.shipTo.city"));
    }

    /// <summary>A tool whose input schema is hand-written: allOf as code generators and other SDKs emit it.</summary>
    internal static McpServerTool AllOfTool()
    {
        McpServerTool tool = McpServerTool.Create((string sku) => sku, new() { Name = "restock", Description = "Restocks a product." });
        tool.ProtocolTool.InputSchema = System.Text.Json.JsonDocument.Parse("""
            {
              "type": "object",
              "properties": { "item": { "$ref": "#/$defs/Item" } },
              "required": ["item"],
              "$defs": {
                "Base": { "type": "object", "properties": { "sku": { "type": "string", "description": "Product SKU" } }, "required": ["sku"] },
                "Item": {
                  "allOf": [
                    { "$ref": "#/$defs/Base" },
                    { "properties": { "quantity": { "type": "integer", "description": "How many" }, "next": { "$ref": "#/$defs/Item" } } }
                  ]
                }
              }
            }
            """).RootElement.Clone();
        return tool;
    }

    [Fact]
    public async Task Markdown_merges_allOf_and_names_a_recursive_type()
    {
        await using TestApp app = await TestApp.StartAsync(server: s => s.WithTools([AllOfTool()]));
        string md = await app.Client().GetStringAsync("/mcp-docs/catalog.md", TestContext.Current.CancellationToken);
        string[] lines = md.Split('\n');
        string Row(string name) => Assert.Single(lines, l => l.StartsWith("| `" + name + "` |"));

        Assert.Contains("| string | yes | Product SKU |", Row("item.sku"));
        Assert.Contains("| integer | no | How many |", Row("item.quantity"));
        Assert.Contains("(recursive: `Item`)", Row("item.next"));
        Assert.DoesNotContain(lines, l => l.StartsWith("| `item.next."));
    }

    [Fact]
    public async Task Llms_txt_is_an_index_that_links_to_the_markdown()
    {
        await using TestApp app = await TestApp.StartAsync();
        HttpResponseMessage res = await app.Client().GetAsync("/mcp-docs/llms.txt", TestContext.Current.CancellationToken);
        Assert.Equal("text/plain", res.Content.Headers.ContentType!.MediaType);
        string text = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("# Test Server", text);
        Assert.Contains("(http://localhost/mcp-docs/catalog.md)", text);
        Assert.Contains("- `get_thing`: Gets a thing.", text);
        Assert.DoesNotContain("delete_thing", text);
    }

    [Fact]
    public async Task Exports_follow_the_PathBase()
    {
        await using TestApp app = await TestApp.StartAsync(beforeDocs: b => b.UsePathBase("/api"));
        string text = await app.Client().GetStringAsync("/api/mcp-docs/llms.txt", TestContext.Current.CancellationToken);
        Assert.Contains("(http://localhost/api/mcp-docs/catalog.md)", text);
        Assert.Contains("at http://localhost/api/mcp.", text);
    }

    [Fact]
    public async Task Server_json_is_only_served_with_a_registry_name()
    {
        await using TestApp without = await TestApp.StartAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await without.Client().GetAsync("/mcp-docs/server.json", TestContext.Current.CancellationToken)).StatusCode);

        await using TestApp app = await TestApp.StartAsync(o => { o.RegistryName = "io.github.acme/test-server"; o.AuthHeader = "Authorization"; o.Description = "Things."; });
        JsonNode json = JsonNode.Parse(await app.Client().GetStringAsync("/mcp-docs/server.json", TestContext.Current.CancellationToken))!;
        Assert.Equal("io.github.acme/test-server", (string?)json["name"]);
        Assert.Equal("Things.", (string?)json["description"]);
        Assert.Equal("1.0.0", (string?)json["version"]);
        JsonNode remote = json["remotes"]![0]!;
        Assert.Equal("streamable-http", (string?)remote["type"]);
        Assert.Equal("http://localhost/mcp", (string?)remote["url"]);
        Assert.Equal("Authorization", (string?)remote["headers"]![0]!["name"]);
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("a/b/c")]
    [InlineData("io acme/x")]
    public async Task Invalid_registry_names_are_rejected(string name) =>
        await Assert.ThrowsAsync<ArgumentException>(() => TestApp.StartAsync(o => o.RegistryName = name));

    [Fact]
    public async Task Exports_inherit_the_group_authorization()
    {
        await using TestApp app = await TestApp.StartAsync(requireAuth: true);
        foreach (string? path in new[] { "catalog.md", "llms.txt" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().GetAsync("/mcp-docs/" + path, TestContext.Current.CancellationToken)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await app.Client("user").GetAsync("/mcp-docs/catalog.md", TestContext.Current.CancellationToken)).StatusCode);
    }
}

public class InstallLinkTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Install_links_can_be_turned_off(bool on)
    {
        await using TestApp app = await TestApp.StartAsync(o => o.InstallLinks = on);
        JsonNode catalog = JsonNode.Parse(await app.Client().GetStringAsync("/mcp-docs/catalog.json", TestContext.Current.CancellationToken))!;
        Assert.Equal(on, (bool)catalog["docs"]!["installLinks"]!);
    }
}
