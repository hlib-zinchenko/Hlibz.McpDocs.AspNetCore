using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Server;

using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

/// <summary>A tool's errors (McpDocsToolErrors): read from its _meta, listed in the catalog and the Markdown export.</summary>
public class ToolErrorTests
{
    // One tool whose server writes the _meta itself (a malformed entry among them), one described through the API.
    private static Task<TestApp> StartAsync(Action<McpDocsOptions>? configure = null) => TestApp.StartAsync(configure, server: mcp =>
    {
        mcp.WithTools(
        [
            McpServerTool.Create(() => "renamed", new()
            {
                Name = "rename_thing",
                Meta = new JsonObject
                {
                    ["owner"] = "team",
                    [McpDocsToolErrors.MetaKey] = new JsonArray(
                        new JsonObject { ["code"] = "thing_not_found", ["guidance"] = "Call list_things to find the {thingId}." },
                        new JsonObject { ["guidance"] = "No code, so left out." },
                        new JsonObject { ["code"] = "name_taken", ["description"] = "Another thing has the name." }),
                },
            }),
            McpServerTool.Create(() => "deleted", new() { Name = "drop_thing" }),
        ]);
        mcp.Services.PostConfigure<McpServerOptions>(o => McpDocsToolErrors.Describe(
            o.ToolCollection!.First(t => t.ProtocolTool.Name == "drop_thing"),
            [new McpDocsToolError("thing_in_use") { Description = "Other things refer to it.", Guidance = "Ask the user first." }]));
    });

    private static async Task<JsonNode> EntryAsync(TestApp app, string tool)
    {
        JsonNode catalog = JsonNode.Parse(await app.Client().GetStringAsync("/mcp-docs/catalog.json", TestContext.Current.CancellationToken))!;
        return catalog["tools"]!.AsArray().First(e => (string?)e!["definition"]!["name"] == tool)!;
    }

    [Fact]
    public async Task Errors_in_meta_are_listed_without_ShowMeta_and_malformed_ones_skipped()
    {
        await using TestApp app = await StartAsync();

        JsonNode entry = await EntryAsync(app, "rename_thing");

        Assert.Null(entry["definition"]!["_meta"]);
        Assert.Equal(
            """[{"code":"thing_not_found","guidance":"Call list_things to find the {thingId}."},{"code":"name_taken","description":"Another thing has the name."}]""",
            entry["errors"]!.ToJsonString());
    }

    [Fact]
    public async Task Describe_writes_the_errors_into_the_tools_meta()
    {
        await using TestApp app = await StartAsync(o => o.ShowMeta = true);

        JsonNode entry = await EntryAsync(app, "drop_thing");

        const string Expected = """[{"code":"thing_in_use","description":"Other things refer to it.","guidance":"Ask the user first."}]""";
        Assert.Equal(Expected, entry["errors"]!.ToJsonString());
        Assert.Equal(Expected, entry["definition"]!["_meta"]![McpDocsToolErrors.MetaKey]!.ToJsonString());
    }

    [Fact]
    public async Task A_tool_without_errors_has_no_errors_entry()
    {
        await using TestApp app = await StartAsync();

        Assert.Null((await EntryAsync(app, "ping"))["errors"]);
    }

    [Fact]
    public async Task Markdown_lists_a_tools_errors_with_the_columns_it_has()
    {
        await using TestApp app = await StartAsync();

        string md = await app.Client().GetStringAsync("/mcp-docs/catalog.md", TestContext.Current.CancellationToken);

        Assert.Contains(
            "Errors (`isError: true`):\n\n| Code | Description | Guidance |\n|---|---|---|\n"
            + "| `thing_not_found` |  | Call list_things to find the {thingId}. |\n"
            + "| `name_taken` | Another thing has the name. |  |\n",
            md);
        Assert.Contains("| `thing_in_use` | Other things refer to it. | Ask the user first. |\n", md);
    }
}
