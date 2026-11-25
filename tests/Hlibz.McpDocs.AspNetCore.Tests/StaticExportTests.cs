using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Server;

using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

/// <summary><see cref="McpDocsSnapshot.CreateHtmlAsync"/>: the page as one file that works without the app.</summary>
public class StaticExportTests
{
    private const string Endpoint = "https://api.example.com/mcp";

    private static JsonObject Catalog(string html)
    {
        Match m = Regex.Match(html, "<script type=\"application/json\" id=\"mcp-catalog\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(m.Success, "No inline catalog.");
        return JsonNode.Parse(m.Groups[1].Value)!.AsObject();
    }

    private static string Csp(string html) =>
        Regex.Match(html, "<meta http-equiv=\"Content-Security-Policy\" content=\"([^\"]*)\">").Groups[1].Value;

    private static string Hash(string inline) => "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(inline))) + "'";

    [Fact]
    public async Task Page_is_one_file_with_the_viewers_catalog()
    {
        await using TestApp app = await TestApp.StartAsync();
        string html = await McpDocsSnapshot.CreateHtmlAsync(app.App.Services, o => o.Title = "Docs <&>", mcpEndpoint: Endpoint);

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<title>Docs &lt;&amp;&gt;</title>", html);
        Assert.DoesNotContain(" src=", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("_assets/", html);

        JsonObject cat = Catalog(html);
        JsonNode d = cat["docs"]!;
        Assert.True(d["static"]!.GetValue<bool>());
        Assert.Equal(Endpoint, d["mcpEndpoint"]!.GetValue<string>());
        Assert.Null(d["stdio"]);

        // The same primitives the JSON snapshot lists, for the same (anonymous) viewer.
        JsonNode snapshot = JsonNode.Parse(await McpDocsSnapshot.CreateAsync(app.App.Services, includePresentation: true))!;
        Assert.True(JsonNode.DeepEquals(snapshot["tools"], cat["tools"]));
        Assert.True(JsonNode.DeepEquals(snapshot["resourceTemplates"], cat["resourceTemplates"]));
        Assert.Contains("delete_thing", html);
    }

    [Fact]
    public async Task With_Hide_the_viewer_decides_what_the_file_shows()
    {
        await using TestApp app = await TestApp.StartAsync();
        ClaimsPrincipal admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester"), new Claim(ClaimTypes.Role, "admin")], "Test"));
        Assert.DoesNotContain("delete_thing", await McpDocsSnapshot.CreateHtmlAsync(app.App.Services, o => o.GatedPrimitives = McpDocsGatedPrimitives.Hide));
        Assert.Contains("delete_thing", await McpDocsSnapshot.CreateHtmlAsync(app.App.Services, o => o.GatedPrimitives = McpDocsGatedPrimitives.Hide, viewer: admin));
    }

    [Fact]
    public async Task Csp_allows_exactly_the_inline_script_and_styles()
    {
        await using TestApp app = await TestApp.StartAsync();
        string html = await McpDocsSnapshot.CreateHtmlAsync(app.App.Services, o => o.PrimaryColor = "#0a7f5a", mcpEndpoint: Endpoint);
        string csp = Csp(html);

        MatchCollection scripts = Regex.Matches(html, "<script>(.*?)</script>", RegexOptions.Singleline);
        MatchCollection styles = Regex.Matches(html, "<style(?: media=\"print\")?>(.*?)</style>", RegexOptions.Singleline);
        Assert.Single(scripts);
        Assert.Equal(2, styles.Count);

        Assert.Equal($"script-src {Hash(scripts[0].Groups[1].Value)};", Regex.Match(csp, "script-src [^;]*;").Value);
        Assert.Equal($"style-src {Hash(styles[0].Groups[1].Value)} {Hash(styles[1].Groups[1].Value)};", Regex.Match(csp, "style-src [^;]*;").Value);
        Assert.StartsWith("default-src 'none';", csp);
        Assert.DoesNotContain("unsafe", csp);
        Assert.Contains("--accent:#0a7f5a", styles[0].Groups[1].Value); // the theme is inline too
    }

    [Fact]
    public async Task Text_that_would_close_a_tag_stays_inside_it()
    {
        const string evil = "Ends here </script><script>alert(1)</script> <!-- and </STYLE>";
        await using TestApp app = await TestApp.StartAsync(server: s => s.WithTools([McpServerTool.Create(() => "x", new() { Name = "evil", Description = evil })]));
        string html = await McpDocsSnapshot.CreateHtmlAsync(app.App.Services, o => o.AdditionalCss = "/* </style><script>alert(2)</script> */");

        Assert.DoesNotContain("alert(1)</script>", html);
        Assert.DoesNotContain("</style><script>", html);
        // Each block ends where the parser would end it (the first closing tag): the CSS keeps its comment, the
        // script is the whole app.js, and the catalog parses (below) with the description intact.
        Assert.Contains("alert(2)", Regex.Match(html, "<style>(.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value);
        Assert.EndsWith("})();\n", Regex.Match(html, "<script>((?:(?!</script>).)*)</script>\n</head>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value);
        JsonNode? tool = Catalog(html)["tools"]!.AsArray().Single(t => t!["definition"]!["name"]!.GetValue<string>() == "evil");
        Assert.Equal(evil, tool!["definition"]!["description"]!.GetValue<string>());
    }

    [Fact]
    public async Task Stdio_server_gets_a_command_instead_of_a_url()
    {
        await using TestApp app = await TestApp.StartAsync();
        string html = await McpDocsSnapshot.CreateHtmlAsync(app.App.Services, stdioCommand: ["dnx", "Acme.Mcp", "--yes"]);

        JsonNode d = Catalog(html)["docs"]!;
        Assert.Null(d["mcpEndpoint"]);
        Assert.Equal("dnx", d["stdio"]!["command"]!.GetValue<string>());
        Assert.Equal(["Acme.Mcp", "--yes"], d["stdio"]!["args"]!.AsArray().Select(a => a!.GetValue<string>()));
    }

    [Fact]
    public async Task Without_endpoint_or_command_there_is_nothing_to_connect_to()
    {
        await using TestApp app = await TestApp.StartAsync();
        JsonNode d = Catalog(await McpDocsSnapshot.CreateHtmlAsync(app.App.Services))["docs"]!;
        Assert.Null(d["mcpEndpoint"]);
        Assert.Null(d["stdio"]);
    }

    [Theory]
    [InlineData("/mcp")]
    [InlineData("mcp")]
    [InlineData("ftp://example.com/mcp")]
    public async Task Endpoint_must_be_an_absolute_http_url(string endpoint)
    {
        await using TestApp app = await TestApp.StartAsync();
        ArgumentException e = await Assert.ThrowsAsync<ArgumentException>(() => McpDocsSnapshot.CreateHtmlAsync(app.App.Services, mcpEndpoint: endpoint));
        Assert.Contains("absolute", e.Message);

        // The same rule when it comes from the options shared with MapMcpDocs.
        await Assert.ThrowsAsync<ArgumentException>(() => McpDocsSnapshot.CreateHtmlAsync(app.App.Services, o => o.McpEndpoint = endpoint));
    }

    [Fact]
    public async Task Options_endpoint_is_used_when_absolute()
    {
        await using TestApp app = await TestApp.StartAsync();
        string html = await McpDocsSnapshot.CreateHtmlAsync(app.App.Services, o => o.McpEndpoint = Endpoint);
        Assert.Equal(Endpoint, Catalog(html)["docs"]!["mcpEndpoint"]!.GetValue<string>());
    }

    [Fact]
    public async Task Endpoint_and_command_together_or_an_empty_command_are_rejected()
    {
        await using TestApp app = await TestApp.StartAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => McpDocsSnapshot.CreateHtmlAsync(app.App.Services, mcpEndpoint: Endpoint, stdioCommand: ["dnx"]));
        await Assert.ThrowsAsync<ArgumentException>(() => McpDocsSnapshot.CreateHtmlAsync(app.App.Services, stdioCommand: []));
        await Assert.ThrowsAsync<ArgumentException>(() => McpDocsSnapshot.CreateHtmlAsync(app.App.Services, stdioCommand: [" "]));
    }

    [Fact]
    public async Task Same_server_and_options_give_the_same_bytes()
    {
        await using TestApp app = await TestApp.StartAsync();
        string a = await McpDocsSnapshot.CreateHtmlAsync(app.App.Services, mcpEndpoint: Endpoint);
        string b = await McpDocsSnapshot.CreateHtmlAsync(app.App.Services, mcpEndpoint: Endpoint);
        Assert.Equal(a, b);
        Assert.DoesNotContain("\r", a);
    }

    [Theory]
    [InlineData("/favicon.png", false)]
    [InlineData("~/favicon.png", false)]
    [InlineData("https://cdn.example.com/f.png", true)]
    public async Task Favicon_paths_into_the_app_are_dropped(string favicon, bool kept)
    {
        await using TestApp app = await TestApp.StartAsync();
        JsonNode d = Catalog(await McpDocsSnapshot.CreateHtmlAsync(app.App.Services, o => o.Favicon = favicon))["docs"]!;
        Assert.Equal(kept, d["favicon"] is not null);
    }

    [Fact]
    public async Task Color_mode_and_print_styles_follow_the_options()
    {
        await using TestApp app = await TestApp.StartAsync();
        string html = await McpDocsSnapshot.CreateHtmlAsync(app.App.Services, o => { o.DefaultColorMode = McpDocsColorMode.Dark; o.PrintStyles = false; });
        Assert.Contains("<html lang=\"en\" data-static data-theme=\"dark\">", html);
        Assert.DoesNotContain("media=\"print\"", html);
    }

    // ---- the page's own download (HtmlExport) ----

    [Fact]
    public async Task Page_offers_its_download_by_default()
    {
        await using TestApp app = await TestApp.StartAsync(o => { o.AddToolbarItem("Changelog", "/changelog"); o.Favicon = "~/f.png"; });
        HttpClient client = app.Client();

        Assert.Contains("data-download=\"docs.html\"", await client.GetStringAsync("/mcp-docs/", TestContext.Current.CancellationToken));

        HttpResponseMessage res = await client.GetAsync("/mcp-docs/docs.html", TestContext.Current.CancellationToken);
        res.EnsureSuccessStatusCode();
        Assert.Equal("text/html", res.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", res.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("test-server-docs.html", res.Content.Headers.ContentDisposition.FileName);

        JsonNode d = Catalog(await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))["docs"]!;
        Assert.True(d["static"]!.GetValue<bool>());
        Assert.Equal("http://localhost/mcp", d["mcpEndpoint"]!.GetValue<string>());     // absolute: the file has no host
        Assert.Equal("http://localhost/changelog", d["links"]![0]!["url"]!.GetValue<string>());
        Assert.Equal("http://localhost/f.png", d["favicon"]!.GetValue<string>());

        // Cached and validated like the other exports.
        HttpRequestMessage again = new HttpRequestMessage(HttpMethod.Get, "/mcp-docs/docs.html");
        again.Headers.TryAddWithoutValidation("If-None-Match", res.Headers.ETag!.Tag);
        Assert.Equal(System.Net.HttpStatusCode.NotModified, (await client.SendAsync(again, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task With_Hide_the_download_shows_what_the_viewer_sees()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.GatedPrimitives = McpDocsGatedPrimitives.Hide);
        Assert.DoesNotContain("delete_thing", await app.Client().GetStringAsync("/mcp-docs/docs.html", TestContext.Current.CancellationToken));
        Assert.Contains("delete_thing", await app.Client("admin").GetStringAsync("/mcp-docs/docs.html", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Download_can_be_turned_off()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.HtmlExport = false);
        HttpClient client = app.Client();
        Assert.DoesNotContain("data-download", await client.GetStringAsync("/mcp-docs/", TestContext.Current.CancellationToken));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/mcp-docs/docs.html", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Download_uses_the_server_key_for_its_file_name()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.ServerKey = "acme");
        HttpResponseMessage res = await app.Client().GetAsync("/mcp-docs/docs.html", TestContext.Current.CancellationToken);
        Assert.Equal("acme-docs.html", res.Content.Headers.ContentDisposition!.FileName);
    }

    [Theory]
    [InlineData("?theme=dark", " data-theme=\"dark\"")]
    [InlineData("?theme=light", " data-theme=\"light\"")]
    [InlineData("?theme=system", "")]
    [InlineData("", "")]
    [InlineData("?theme=bogus", "")]
    public async Task Download_opens_in_the_theme_the_viewer_picked(string query, string attribute)
    {
        await using TestApp app = await TestApp.StartAsync();
        string html = await app.Client().GetStringAsync("/mcp-docs/docs.html" + query, TestContext.Current.CancellationToken);
        Assert.Contains("<html lang=\"en\" data-static" + attribute + ">", html);
    }

    [Fact]
    public async Task Without_a_pick_the_download_uses_the_default_mode()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.DefaultColorMode = McpDocsColorMode.Dark);
        Assert.Contains("<html lang=\"en\" data-static data-theme=\"dark\">", await app.Client().GetStringAsync("/mcp-docs/docs.html", TestContext.Current.CancellationToken));
        Assert.Contains("<html lang=\"en\" data-static data-theme=\"light\">", await app.Client().GetStringAsync("/mcp-docs/docs.html?theme=light", TestContext.Current.CancellationToken));
        // A viewer who switched back to following the system gets a file that follows it too.
        Assert.Contains("<html lang=\"en\" data-static>", await app.Client().GetStringAsync("/mcp-docs/docs.html?theme=system", TestContext.Current.CancellationToken));
    }
}
