using System.Net;
using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

public class AuthorizationTests
{
    [Theory]
    [InlineData("/mcp-docs/")]
    [InlineData("/mcp-docs/catalog.json")]
    [InlineData("/mcp-docs/_assets/app.js")]
    public async Task Protected_docs_reject_anonymous_callers_and_accept_signed_in_ones(string path)
    {
        await using var app = await TestApp.StartAsync(requireAuth: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Client("user").GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Gated_tools_are_hidden_from_tools_list_for_anonymous_callers_too()
    {
        await using var app = await TestApp.StartAsync();
        await using var anon = await app.McpAsync();
        Assert.DoesNotContain(await anon.ListToolsAsync(), t => t.Name == "delete_thing");
        await using var admin = await app.McpAsync("admin");
        Assert.Contains(await admin.ListToolsAsync(), t => t.Name == "delete_thing");
    }
}
