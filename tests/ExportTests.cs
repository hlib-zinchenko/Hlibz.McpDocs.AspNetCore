using Microsoft.AspNetCore.Builder;
using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

public class ExportTests
{
    [Fact]
    public async Task Markdown_lists_the_catalog_and_hides_what_the_viewer_may_not_see()
    {
        await using var app = await TestApp.StartAsync();
        var anon = await app.Client().GetStringAsync("/mcp-docs/catalog.md");
        var admin = await app.Client("admin").GetStringAsync("/mcp-docs/catalog.md");

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
    public async Task Llms_txt_is_an_index_that_links_to_the_markdown()
    {
        await using var app = await TestApp.StartAsync();
        var res = await app.Client().GetAsync("/mcp-docs/llms.txt");
        Assert.Equal("text/plain", res.Content.Headers.ContentType!.MediaType);
        var text = await res.Content.ReadAsStringAsync();
        Assert.StartsWith("# Test Server", text);
        Assert.Contains("(http://localhost/mcp-docs/catalog.md)", text);
        Assert.Contains("- `get_thing`: Gets a thing.", text);
        Assert.DoesNotContain("delete_thing", text);
    }

    [Fact]
    public async Task Exports_follow_the_PathBase()
    {
        await using var app = await TestApp.StartAsync(beforeDocs: b => b.UsePathBase("/api"));
        var text = await app.Client().GetStringAsync("/api/mcp-docs/llms.txt");
        Assert.Contains("(http://localhost/api/mcp-docs/catalog.md)", text);
        Assert.Contains("at http://localhost/api/mcp.", text);
    }

    [Fact]
    public async Task Server_json_is_only_served_with_a_registry_name()
    {
        await using var without = await TestApp.StartAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await without.Client().GetAsync("/mcp-docs/server.json")).StatusCode);

        await using var app = await TestApp.StartAsync(o => { o.RegistryName = "io.github.acme/test-server"; o.AuthHeader = "Authorization"; o.Description = "Things."; });
        var json = JsonNode.Parse(await app.Client().GetStringAsync("/mcp-docs/server.json"))!;
        Assert.Equal("io.github.acme/test-server", (string?)json["name"]);
        Assert.Equal("Things.", (string?)json["description"]);
        Assert.Equal("1.0.0", (string?)json["version"]);
        var remote = json["remotes"]![0]!;
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
        await using var app = await TestApp.StartAsync(requireAuth: true);
        foreach (var path in new[] { "catalog.md", "llms.txt" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().GetAsync("/mcp-docs/" + path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Client("user").GetAsync("/mcp-docs/catalog.md")).StatusCode);
    }
}

public class InstallLinkTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Install_links_can_be_turned_off(bool on)
    {
        await using var app = await TestApp.StartAsync(o => o.InstallLinks = on);
        var catalog = JsonNode.Parse(await app.Client().GetStringAsync("/mcp-docs/catalog.json"))!;
        Assert.Equal(on, (bool)catalog["docs"]!["installLinks"]!);
    }
}
