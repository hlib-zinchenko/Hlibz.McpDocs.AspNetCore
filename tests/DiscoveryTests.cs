using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

public class DiscoveryTests
{
    [Fact]
    public async Task Finds_the_mapped_route_when_nothing_is_configured()
    {
        await using var app = await TestApp.StartAsync();
        var r = McpDocsEndpointDiscovery.Resolve(app.App.Services, new McpDocsOptions(), "/mcp-docs");
        Assert.Equal("/mcp", r.Endpoint);
        Assert.Empty(r.Errors);
    }

    [Fact]
    public async Task A_configured_path_that_matches_no_route_is_an_error()
    {
        await using var app = await TestApp.StartAsync();
        var r = McpDocsEndpointDiscovery.Resolve(app.App.Services, new McpDocsOptions { McpEndpoint = "/nope" }, "/mcp-docs");
        Assert.NotEmpty(r.Errors);
        Assert.Contains("/nope", r.Errors[0]);
    }

    [Fact]
    public async Task A_configured_path_that_matches_is_accepted()
    {
        await using var app = await TestApp.StartAsync();
        var r = McpDocsEndpointDiscovery.Resolve(app.App.Services, new McpDocsOptions { McpEndpoint = "/mcp" }, "/mcp-docs");
        Assert.Empty(r.Errors);
    }
}
