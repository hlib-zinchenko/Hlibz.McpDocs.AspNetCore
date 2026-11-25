using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

/// <summary>
/// <see cref="McpDocsOptions.EnableTryIt"/>: what the catalog tells the page, where the console can't run, and the
/// request the console sends, posted here exactly as app.js builds it, so a change in the SDK's wire format fails
/// here first (the weekly newest-SDK run included).
/// </summary>
public class TryItTests
{
    private static async Task<JsonNode?> TryItAsync(TestApp app)
    {
        string json = await app.Client().GetStringAsync("/mcp-docs/catalog.json", TestContext.Current.CancellationToken);
        return JsonNode.Parse(json)!["docs"]!["tryIt"];
    }

    [Fact]
    public async Task The_console_is_off_by_default()
    {
        await using TestApp app = await TestApp.StartAsync();
        Assert.Null(await TryItAsync(app));
        Assert.Null(new McpDocsOptions().TryIt);
    }

    [Fact]
    public async Task EnableTryIt_puts_its_defaults_in_the_catalog()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.EnableTryIt());
        JsonNode t = (await TryItAsync(app))!;
        Assert.Equal("memory", (string?)t["credentialStorage"]);
        Assert.Equal(8 * 3600, (long?)t["credentialLifetime"]);
        Assert.True((bool?)t["sendCookies"]);
        Assert.False((bool?)t["readOnlyOnly"]);
        Assert.Null(t["unavailable"]);
    }

    [Fact]
    public async Task Configured_settings_reach_the_catalog_and_a_second_call_builds_on_the_first()
    {
        await using TestApp app = await TestApp.StartAsync(o => o
            .EnableTryIt(t => { t.CredentialStorage = McpDocsTryItCredentialStorage.Local; t.CredentialLifetime = TimeSpan.FromMinutes(90); })
            .EnableTryIt(t => { t.SendCookies = false; t.ReadOnlyOnly = true; }));
        JsonNode t = (await TryItAsync(app))!;
        Assert.Equal("local", (string?)t["credentialStorage"]);
        Assert.Equal(5400, (long?)t["credentialLifetime"]);
        Assert.False((bool?)t["sendCookies"]);
        Assert.True((bool?)t["readOnlyOnly"]);
    }

    [Fact]
    public async Task A_token_kept_until_forgotten_has_no_lifetime()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.EnableTryIt(t => { t.CredentialStorage = McpDocsTryItCredentialStorage.Session; t.CredentialLifetime = null; }));
        JsonNode t = (await TryItAsync(app))!;
        Assert.Equal("session", (string?)t["credentialStorage"]);
        Assert.False(t.AsObject().ContainsKey("credentialLifetime"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_lifetime_that_is_not_positive_throws(int seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new McpDocsTryItOptions { CredentialLifetime = TimeSpan.FromSeconds(seconds) });

    [Fact]
    public async Task A_stateful_server_gets_no_console_and_a_startup_warning()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.EnableTryIt(),
            server: s => s.Services.Configure<HttpServerTransportOptions>(o => o.SessionMode = HttpServerSessionMode.Stateful));
        Assert.Contains("stateful", (string?)(await TryItAsync(app))!["unavailable"]);

        McpDocsOptions docs = new McpDocsOptions().EnableTryIt();
        McpDocsEndpointDiscovery.Result r = McpDocsEndpointDiscovery.Resolve(app.App.Services, docs, "/mcp-docs");
        Assert.Contains(r.Warnings, w => w.StartsWith("EnableTryIt has no effect", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateless)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    public async Task Stateless_and_hybrid_servers_keep_the_console(HttpServerSessionMode mode)
    {
        await using TestApp app = await TestApp.StartAsync(o => o.EnableTryIt(),
            server: s => s.Services.Configure<HttpServerTransportOptions>(o => o.SessionMode = mode));
        Assert.Null((await TryItAsync(app))!["unavailable"]);
        Assert.DoesNotContain(McpDocsEndpointDiscovery.Resolve(app.App.Services, new McpDocsOptions().EnableTryIt(), "/mcp-docs").Warnings,
            w => w.Contains("try-it", StringComparison.OrdinalIgnoreCase) || w.Contains("TryIt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_absolute_endpoint_warns_that_the_console_only_calls_its_own_origin()
    {
        await using TestApp app = await TestApp.StartAsync();
        McpDocsOptions docs = new McpDocsOptions { McpEndpoint = "https://gateway.example.com/mcp" }.EnableTryIt();
        Assert.Contains(McpDocsEndpointDiscovery.Resolve(app.App.Services, docs, "/mcp-docs").Warnings, w => w.Contains("own origin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Static_exports_leave_the_console_out()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.EnableTryIt());
        string served = await app.Client().GetStringAsync("/mcp-docs/docs.html", TestContext.Current.CancellationToken);
        string file = await McpDocsSnapshot.CreateHtmlAsync(app.App.Services, o => o.EnableTryIt(), mcpEndpoint: "https://api.example.com/mcp");
        foreach (string html in new[] { served, file })
        {
            Match m = Regex.Match(html, "<script type=\"application/json\" id=\"mcp-catalog\">(.*?)</script>", RegexOptions.Singleline);
            JsonObject docs = JsonNode.Parse(m.Groups[1].Value)!["docs"]!.AsObject();
            Assert.True((bool?)docs["static"]);
            Assert.False(docs.ContainsKey("tryIt"));
        }
    }

    // ---- the wire format app.js uses (buildRequest, headerValue, paramHeaders, parseMessages)

    private static HttpRequestMessage ConsoleRequest(string method, string name, JsonObject @params, IDictionary<string, string>? headers = null)
    {
        @params["_meta"] = new JsonObject
        {
            ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
            ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject(),
            ["io.modelcontextprotocol/clientInfo"] = new JsonObject { ["name"] = "mcp-docs-try-it", ["version"] = "1.0.0" },
        };
        JsonObject body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = @params };
        HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        req.Headers.Add("Mcp-Method", method);
        req.Headers.Add("Mcp-Name", name);
        foreach (KeyValuePair<string, string> h in headers ?? new Dictionary<string, string>())
        {
            req.Headers.Add(h.Key, h.Value);
        }

        return req;
    }

    // The message answering id 1, from an SSE or JSON body, as parseMessages reads it.
    private static async Task<(HttpStatusCode Status, JsonNode? Answer)> SendAsync(TestApp app, HttpRequestMessage req)
    {
        HttpResponseMessage res = await app.Client().SendAsync(req, TestContext.Current.CancellationToken);
        string text = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        IEnumerable<string> payloads = res.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.ReplaceLineEndings("\n").Split("\n\n").Select(b => string.Join("\n", b.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart(' ')))).Where(d => d.Length > 0)
            : text.Length > 0 ? [text] : [];
        JsonNode? answer = payloads.Select(p => JsonNode.Parse(p)).FirstOrDefault(m => (int?)m?["id"] == 1);
        return (res.StatusCode, answer);
    }

    [Fact]
    public async Task A_tool_call_in_the_consoles_shape_is_answered()
    {
        await using TestApp app = await TestApp.StartAsync();
        (HttpStatusCode status, JsonNode? answer) = await SendAsync(app, ConsoleRequest("tools/call", "get_thing", new JsonObject { ["name"] = "get_thing", ["arguments"] = new JsonObject { ["id"] = "t-9" } }));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("t-9", (string?)answer!["result"]!["content"]![0]!["text"]);
    }

    [Fact]
    public async Task Prompt_and_resource_template_requests_in_the_consoles_shape_are_answered()
    {
        await using TestApp app = await TestApp.StartAsync();
        (_, JsonNode? prompt) = await SendAsync(app, ConsoleRequest("prompts/get", "greet", new JsonObject { ["name"] = "greet", ["arguments"] = new JsonObject { ["name"] = "Ann" } }));
        Assert.Equal("Hello Ann", (string?)prompt!["result"]!["messages"]![0]!["content"]!["text"]);

        // expandUri percent-encodes a simple variable; Mcp-Name carries the expanded URI.
        (_, JsonNode? resource) = await SendAsync(app, ConsoleRequest("resources/read", "thing://a%20b", new JsonObject { ["uri"] = "thing://a%20b" }));
        Assert.Equal("a b", (string?)resource!["result"]!["contents"]![0]!["text"]);
    }

    [Fact]
    public async Task A_gated_tool_is_refused_as_a_json_rpc_error_and_allowed_with_the_role()
    {
        await using TestApp app = await TestApp.StartAsync();
        JsonObject Params() => new JsonObject { ["name"] = "delete_thing", ["arguments"] = new JsonObject { ["id"] = "x" } };
        (HttpStatusCode status, JsonNode? refused) = await SendAsync(app, ConsoleRequest("tools/call", "delete_thing", Params()));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(refused!["error"]);

        (_, JsonNode? allowed) = await SendAsync(app, ConsoleRequest("tools/call", "delete_thing", Params(), new Dictionary<string, string> { ["X-Roles"] = "admin" }));
        Assert.Equal("x", (string?)allowed!["result"]!["content"]![0]!["text"]);
    }

    [Fact]
    public async Task McpHeader_parameters_must_be_mirrored_into_Mcp_Param_headers()
    {
        McpServerTool regional = McpServerTool.Create(([McpHeader("Region")] string region) => "in " + region, new() { Name = "regional", ReadOnly = true });
        await using TestApp app = await TestApp.StartAsync(server: s => s.WithTools([regional]));

        // What paramHeaders reads: x-mcp-header on the property.
        string schema = await app.Client().GetStringAsync("/mcp-docs/catalog.json", TestContext.Current.CancellationToken);
        JsonNode tool = JsonNode.Parse(schema)!["tools"]!.AsArray().First(e => (string?)e!["definition"]!["name"] == "regional")!;
        Assert.Equal("Region", (string?)tool["definition"]!["inputSchema"]!["properties"]!["region"]!["x-mcp-header"]);

        JsonObject Params() => new JsonObject { ["name"] = "regional", ["arguments"] = new JsonObject { ["region"] = "eu-west" } };
        (_, JsonNode? withHeader) = await SendAsync(app, ConsoleRequest("tools/call", "regional", Params(), new Dictionary<string, string> { ["Mcp-Param-Region"] = "eu-west" }));
        Assert.Equal("in eu-west", (string?)withHeader!["result"]!["content"]![0]!["text"]);

        (HttpStatusCode status, JsonNode? without) = await SendAsync(app, ConsoleRequest("tools/call", "regional", Params()));
        Assert.True(status != HttpStatusCode.OK || without?["error"] is not null, "The server accepted a call without its Mcp-Param header.");
    }

    [Fact]
    public async Task Non_ascii_header_values_go_base64_encoded()
    {
        McpServerTool regional = McpServerTool.Create(([McpHeader("Region")] string region) => "in " + region, new() { Name = "regional", ReadOnly = true });
        await using TestApp app = await TestApp.StartAsync(server: s => s.WithTools([regional]));

        // headerValue("Zürich"): =?base64?{UTF-8 bytes}?=
        string encoded = "=?base64?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Zürich")) + "?=";
        (_, JsonNode? answer) = await SendAsync(app, ConsoleRequest("tools/call", "regional",
            new JsonObject { ["name"] = "regional", ["arguments"] = new JsonObject { ["region"] = "Zürich" } },
            new Dictionary<string, string> { ["Mcp-Param-Region"] = encoded }));
        Assert.Equal("in Zürich", (string?)answer!["result"]!["content"]![0]!["text"]);
    }
}
