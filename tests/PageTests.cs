using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

public class PageTests
{
    [Fact]
    public async Task Page_is_served_with_a_strict_default_CSP()
    {
        await using var app = await TestApp.StartAsync();
        var res = await app.Client().GetAsync("/mcp-docs/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var csp = string.Join(";", res.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.Contains("_assets/app.js", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_existing_CSP_is_kept()
    {
        await using var app = await TestApp.StartAsync(beforeDocs: b => b.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'self'";
            await next();
        }));
        var res = await app.Client().GetAsync("/mcp-docs/");
        Assert.Equal("default-src 'self'", Assert.Single(res.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task Path_without_trailing_slash_redirects()
    {
        await using var app = await TestApp.StartAsync();
        var res = await app.Client().GetAsync("/mcp-docs");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.EndsWith("/mcp-docs/", res.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Default_color_mode_is_set_on_the_html_element()
    {
        await using var app = await TestApp.StartAsync(o => o.DefaultColorMode = McpDocsColorMode.Dark);
        Assert.Contains("<html lang=\"en\" data-theme=\"dark\">", await app.Client().GetStringAsync("/mcp-docs/"));
        await using var plain = await TestApp.StartAsync();
        Assert.Contains("<html lang=\"en\">", await plain.Client().GetStringAsync("/mcp-docs/"));
    }

    [Fact]
    public async Task Theme_css_carries_the_options_and_is_linked_only_when_needed()
    {
        await using var plain = await TestApp.StartAsync();
        Assert.DoesNotContain("theme.css", await plain.Client().GetStringAsync("/mcp-docs/"));

        await using var app = await TestApp.StartAsync(o =>
        {
            o.PrimaryColor = "#0a7f5a";
            o.DarkPreset = McpDocsDarkPreset.Nord;
            o.AdditionalCss = "h1 { color: red; }";
        });
        var html = await app.Client().GetStringAsync("/mcp-docs/");
        var href = System.Text.RegularExpressions.Regex.Match(html, "href=\"(_assets/theme.css[^\"]*)\"").Groups[1].Value;
        var css = await app.Client().GetStringAsync("/mcp-docs/" + href);
        Assert.Contains("--accent:#0a7f5a", css);
        Assert.Contains("#2e3440", css);
        Assert.Contains("h1 { color: red; }", css);
    }

    [Fact]
    public async Task Print_styles_can_be_switched_off()
    {
        await using var on = await TestApp.StartAsync();
        Assert.Contains("media=\"print\"", await on.Client().GetStringAsync("/mcp-docs/"));
        await using var off = await TestApp.StartAsync(o => o.PrintStyles = false);
        Assert.DoesNotContain("print.css", await off.Client().GetStringAsync("/mcp-docs/"));
    }

    [Theory]
    [InlineData("red; } body { display:none")]
    [InlineData("url(https://evil.example/x)")]
    [InlineData("#12")]
    public async Task Colors_that_could_break_out_of_the_declaration_stop_startup(string color)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => TestApp.StartAsync(o => o.PrimaryColor = color));
        await Assert.ThrowsAsync<ArgumentException>(() => TestApp.StartAsync(o => o.DarkModePrimaryColor = color));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.example")]
    [InlineData("data:text/html,x")]
    public void Unsafe_toolbar_urls_are_rejected(string url) =>
        Assert.Throws<ArgumentException>(() => new McpDocsOptions().AddToolbarItem("x", url));

    [Fact]
    public async Task Toolbar_links_and_favicon_resolve_against_PathBase_in_the_catalog()
    {
        await using var app = await TestApp.StartAsync(
            o => { o.AddToolbarItem("Status", "~/status", newTab: false); o.Favicon = "~/fav.ico"; },
            beforeDocs: b => b.UsePathBase("/app"));
        var json = await app.Client().GetStringAsync("/app/mcp-docs/catalog.json");
        Assert.Contains("\"url\":\"/app/status\"", json);
        Assert.Contains("\"favicon\":\"/app/fav.ico\"", json);
        Assert.Contains("\"mcpEndpoint\":\"/app/mcp\"", json);
    }

    [Fact]
    public async Task Assets_are_immutable_and_unknown_assets_404()
    {
        await using var app = await TestApp.StartAsync();
        var js = await app.Client().GetAsync("/mcp-docs/_assets/app.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Contains("immutable", js.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client().GetAsync("/mcp-docs/_assets/nope.js")).StatusCode);
    }
}
