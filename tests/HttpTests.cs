using System.Net;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

/// <summary>Conditional requests, HEAD, redirects and what a misconfigured page does.</summary>
public class HttpTests
{
    private static async Task<HttpStatusCode> GetWithIfNoneMatch(HttpClient http, string path, string ifNoneMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        return (await http.SendAsync(req)).StatusCode;
    }

    [Theory]
    [InlineData("/mcp-docs/catalog.json")]
    [InlineData("/mcp-docs/catalog.md")]
    [InlineData("/mcp-docs/llms.txt")]
    [InlineData("/mcp-docs/_assets/app.js")]
    public async Task Weak_and_wildcard_validators_match(string path)
    {
        await using var app = await TestApp.StartAsync();
        var http = app.Client();
        var etag = (await http.GetAsync(path)).Headers.ETag!.Tag;

        Assert.Equal(HttpStatusCode.NotModified, await GetWithIfNoneMatch(http, path, etag));
        Assert.Equal(HttpStatusCode.NotModified, await GetWithIfNoneMatch(http, path, "W/" + etag));
        Assert.Equal(HttpStatusCode.NotModified, await GetWithIfNoneMatch(http, path, "\"other\", W/" + etag));
        Assert.Equal(HttpStatusCode.NotModified, await GetWithIfNoneMatch(http, path, "*"));
        Assert.Equal(HttpStatusCode.OK, await GetWithIfNoneMatch(http, path, "\"other\""));
    }

    [Theory]
    [InlineData("/mcp-docs/")]
    [InlineData("/mcp-docs/catalog.json")]
    [InlineData("/mcp-docs/catalog.md")]
    [InlineData("/mcp-docs/llms.txt")]
    [InlineData("/mcp-docs/_assets/app.css")]
    public async Task Head_returns_the_headers_without_a_body(string path)
    {
        await using var app = await TestApp.StartAsync();
        var http = app.Client();
        var get = await http.GetAsync(path);
        var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));

        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(get.Content.Headers.ContentType, head.Content.Headers.ContentType);
        Assert.Equal((await get.Content.ReadAsByteArrayAsync()).Length, head.Content.Headers.ContentLength);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Exports_are_stable_across_requests()
    {
        await using var app = await TestApp.StartAsync();
        var http = app.Client();
        var first = await http.GetAsync("/mcp-docs/catalog.md");
        var second = await http.GetAsync("/mcp-docs/catalog.md");
        Assert.Equal(first.Headers.ETag, second.Headers.ETag);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        // Same catalog, other origin: its own links.
        var other = app.Client();
        other.DefaultRequestHeaders.Host = "docs.example";
        Assert.Contains("(http://docs.example/mcp-docs/catalog.md)", await other.GetStringAsync("/mcp-docs/llms.txt"));
    }

    [Fact]
    public async Task Docs_at_the_root_of_a_PathBase_redirect_to_a_single_slash()
    {
        await using var app = await TestApp.StartAsync(docsPattern: "/", beforeDocs: b => b.UsePathBase("/app"));
        var res = await app.Client().GetAsync("/app");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/app/", res.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await app.Client().GetAsync("/app/")).StatusCode);
        Assert.Contains("(http://localhost/app/catalog.md)", await app.Client().GetStringAsync("/app/llms.txt"));
    }

    [Fact]
    public async Task A_wrong_McpEndpoint_fails_the_docs_without_touching_the_process_exit_code()
    {
        var exitCode = Environment.ExitCode;
        await using var app = await TestApp.StartAsync(o => o.McpEndpoint = "/nope");

        Assert.Equal(exitCode, Environment.ExitCode);
        Assert.True(app.App.Lifetime.ApplicationStopping.IsCancellationRequested, "the app should be stopping");
        // TestServer keeps answering after StopApplication; what it answers must not be the wrong snippets.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Client().GetAsync("/mcp-docs/catalog.json"));
        Assert.Contains("/nope", ex.Message);
    }

    [Theory]
    [InlineData("\"abc\"", "\"abc\"", true)]
    [InlineData("W/\"abc\"", "\"abc\"", true)]
    [InlineData(" \"x\" , W/\"abc\" ", "\"abc\"", true)]
    [InlineData("*", "\"abc\"", true)]
    [InlineData("\"abcd\"", "\"abc\"", false)]
    [InlineData("", "\"abc\"", false)]
    public void If_none_match_uses_weak_comparison(string header, string etag, bool match) =>
        Assert.Equal(match, McpDocsEndpointRouteBuilderExtensions.NoneMatch(header, etag));
}
