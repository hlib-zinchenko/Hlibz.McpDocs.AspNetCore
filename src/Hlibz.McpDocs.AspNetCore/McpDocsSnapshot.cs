using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>
/// Builds the same catalog the docs page shows, without an HTTP request, so a test can snapshot it
/// (e.g. with Verify, or by comparing to a checked-in file). A renamed parameter, a changed output schema or
/// a dropped <c>[Authorize]</c> then fails CI: the MCP equivalent of an OpenAPI diff.
/// </summary>
public static class McpDocsSnapshot
{
    /// <summary>
    /// The catalog as indented JSON, in a stable order, as <paramref name="viewer"/> would see it
    /// (anonymous when <see langword="null"/>; primitives gated with <c>[Authorize]</c> are left out unless the viewer passes).
    /// </summary>
    /// <param name="services">The app's services, e.g. <c>app.Services</c> or <c>factory.Services</c>.</param>
    /// <param name="configure">The same configuration passed to <c>MapMcpDocs</c>; only options that change the catalog matter.</param>
    /// <param name="viewer">Who is looking, to snapshot a role's view.</param>
    /// <param name="mcpEndpoint">What to record as the MCP endpoint (default <c>/mcp</c>); no routing is consulted.</param>
    public static async Task<string> CreateAsync(
        IServiceProvider services,
        Action<McpDocsOptions>? configure = null,
        ClaimsPrincipal? viewer = null,
        string mcpEndpoint = "/mcp")
    {
        ArgumentNullException.ThrowIfNull(services);
        var docs = new McpDocsOptions { McpEndpoint = mcpEndpoint };
        configure?.Invoke(docs);
        docs.Validate();

        var server = services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var catalog = new McpDocsCatalog(server, docs, docs.McpEndpoint ?? mcpEndpoint);

        using var scope = services.CreateScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = viewer ?? new ClaimsPrincipal(new ClaimsIdentity()) };
        var result = await catalog.GetAsync(ctx);

        var node = JsonNode.Parse(result.Json)!;
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }
}
