using System.Text.Json;
using System.Text.Json.Nodes;

using ModelContextProtocol;
using ModelContextProtocol.Client;

using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

public class CatalogTests
{
    private static async Task<JsonNode> CatalogAsync(TestApp app, string? roles = null)
    {
        string json = await app.Client(roles).GetStringAsync("/mcp-docs/catalog.json");
        return JsonNode.Parse(json)!;
    }

    private static JsonNode? Definition(JsonNode catalog, string section, string name) =>
        catalog[section]!.AsArray().Select(e => e!["definition"]!).FirstOrDefault(d => (string?)(d["name"]) == name);

    private static JsonNode Serialize<T>(T value) =>
        JsonSerializer.SerializeToNode(value, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T)))!;

    [Theory]
    [InlineData(null)]
    [InlineData("admin")]
    public async Task Catalog_matches_the_lists_the_mcp_endpoint_returns(string? roles)
    {
        await using TestApp app = await TestApp.StartAsync();
        JsonNode catalog = await CatalogAsync(app, roles);
        await using McpClient client = await app.McpAsync(roles);

        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        IList<McpClientPrompt> prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);
        IList<McpClientResource> resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        IList<McpClientResourceTemplate> templates = await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(tools); Assert.NotEmpty(prompts); Assert.NotEmpty(resources); Assert.NotEmpty(templates);
        Assert.Equal(tools.Count, catalog["tools"]!.AsArray().Count);
        foreach (McpClientTool t in tools)
        {
            Assert.True(JsonNode.DeepEquals(Serialize(t.ProtocolTool).AsObject().Without("_meta"), Definition(catalog, "tools", t.Name)), $"tool {t.Name} differs");
        }

        Assert.Equal(prompts.Count, catalog["prompts"]!.AsArray().Count);
        foreach (McpClientPrompt p in prompts)
        {
            Assert.True(JsonNode.DeepEquals(Serialize(p.ProtocolPrompt).AsObject().Without("_meta"), Definition(catalog, "prompts", p.Name)), $"prompt {p.Name} differs");
        }

        Assert.Equal(resources.Count, catalog["resources"]!.AsArray().Count);
        foreach (McpClientResource r in resources)
        {
            Assert.True(JsonNode.DeepEquals(Serialize(r.ProtocolResource).AsObject().Without("_meta"), Definition(catalog, "resources", r.Name)), $"resource {r.Name} differs");
        }

        Assert.Equal(templates.Count, catalog["resourceTemplates"]!.AsArray().Count);
        foreach (McpClientResourceTemplate t in templates)
        {
            Assert.True(JsonNode.DeepEquals(Serialize(t.ProtocolResourceTemplate).AsObject().Without("_meta"), Definition(catalog, "resourceTemplates", t.Name)), $"template {t.Name} differs");
        }
    }

    [Fact]
    public async Task Gated_tools_are_hidden_from_anonymous_viewers_and_marked_for_allowed_ones()
    {
        await using TestApp app = await TestApp.StartAsync();

        JsonNode anon = await CatalogAsync(app);
        Assert.Null(Definition(anon, "tools", "delete_thing"));
        Assert.NotNull(Definition(anon, "tools", "get_thing"));

        JsonNode admin = await CatalogAsync(app, "admin");
        Assert.NotNull(Definition(admin, "tools", "delete_thing"));
        JsonNode entry = admin["tools"]!.AsArray().Single(e => (string?)e!["definition"]!["name"] == "delete_thing")!;
        Assert.NotNull(entry["authorization"]);
        Assert.Contains("admin", entry["authorization"]!.ToJsonString());
    }

    [Fact]
    public async Task Meta_is_left_out_unless_ShowMeta_is_set()
    {
        await using (TestApp off = await TestApp.StartAsync())
        {
            Assert.Null(Definition(await CatalogAsync(off), "tools", "ping")!["_meta"]);
        }

        await using TestApp on = await TestApp.StartAsync(o => o.ShowMeta = true);
        Assert.Equal("team", (string?)Definition(await CatalogAsync(on), "tools", "ping")!["_meta"]!["owner"]);
    }

    [Fact]
    public async Task Description_issues_are_reported_and_can_be_switched_off()
    {
        await using (TestApp app = await TestApp.StartAsync())
        {
            JsonNode catalog = await CatalogAsync(app);
            JsonNode sloppy = catalog["tools"]!.AsArray().Single(e => (string?)e!["definition"]!["name"] == "sloppy")!;
            List<string?> issues = sloppy["issues"]!.AsArray().Select(i => (string?)i).ToList();
            Assert.Contains(issues, i => i!.StartsWith("No description"));
            Assert.Contains(issues, i => i!.StartsWith("Parameters without a description"));
            Assert.Null(catalog["tools"]!.AsArray().Single(e => (string?)e!["definition"]!["name"] == "get_thing")!["issues"]);
        }
        await using TestApp off = await TestApp.StartAsync(o => o.CheckDescriptions = false);
        Assert.DoesNotContain("issues", await off.Client().GetStringAsync("/mcp-docs/catalog.json", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Endpoint_is_discovered_from_MapMcp()
    {
        await using TestApp app = await TestApp.StartAsync();
        Assert.Equal("/mcp", (string?)(await CatalogAsync(app))["docs"]!["mcpEndpoint"]);
    }

    [Fact]
    public async Task Catalog_supports_conditional_requests()
    {
        await using TestApp app = await TestApp.StartAsync();
        HttpClient http = app.Client();
        HttpResponseMessage first = await http.GetAsync("/mcp-docs/catalog.json", TestContext.Current.CancellationToken);
        string etag = first.Headers.ETag!.Tag;
        HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, "/mcp-docs/catalog.json");
        req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(System.Net.HttpStatusCode.NotModified, (await http.SendAsync(req, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Contains("private", first.Headers.CacheControl!.ToString());
    }
}

internal static class JsonExtensions
{
    public static JsonObject Without(this JsonObject o, string name) { o.Remove(name); return o; }
}
