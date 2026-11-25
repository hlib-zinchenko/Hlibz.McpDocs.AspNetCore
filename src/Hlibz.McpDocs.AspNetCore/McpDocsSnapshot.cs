using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>
/// Builds the same catalog the docs page shows, without an HTTP request: as JSON, so a test can snapshot it
/// (e.g. with Verify, or by comparing to a checked-in file), or as a self-contained HTML page to publish anywhere.
/// A renamed parameter, a changed output schema or a dropped <c>[Authorize]</c> then fails CI: the MCP equivalent
/// of an OpenAPI diff.
/// </summary>
public static class McpDocsSnapshot
{
    /// <summary>
    /// The catalog as indented JSON, in a stable order, as <paramref name="viewer"/> would see it. By default every
    /// primitive is listed, gated ones with what they require, whoever the viewer is; with
    /// <see cref="McpDocsGatedPrimitives.Hide"/> primitives gated with <c>[Authorize]</c> are left out unless the
    /// viewer (anonymous when <see langword="null"/>) passes.
    /// <para>
    /// By default only the contract: what clients get from the server (definitions, capabilities, instructions) and
    /// what a viewer needs to call it (authorization). How the page presents it is left out (the <c>docs</c> block,
    /// sidebar groups, description warnings), so a new title or toolbar link doesn't fail a contract test.
    /// </para>
    /// </summary>
    /// <param name="services">The app's services, e.g. <c>app.Services</c> or <c>factory.Services</c>.</param>
    /// <param name="configure">The same configuration passed to <c>MapMcpDocs</c>; only options that change the catalog matter.</param>
    /// <param name="viewer">Who is looking, to snapshot a role's view. Matters only with <see cref="McpDocsGatedPrimitives.Hide"/>.</param>
    /// <param name="mcpEndpoint">What to record as the MCP endpoint (default <c>/mcp</c>) when <paramref name="includePresentation"/> is set; no routing is consulted.</param>
    /// <param name="includePresentation">Also include the presentation: the whole catalog the page is built from.</param>
    public static async Task<string> CreateAsync(
        IServiceProvider services,
        Action<McpDocsOptions>? configure = null,
        ClaimsPrincipal? viewer = null,
        string mcpEndpoint = "/mcp",
        bool includePresentation = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        McpDocsOptions docs = new McpDocsOptions { McpEndpoint = mcpEndpoint };
        configure?.Invoke(docs);
        docs.Validate();

        JsonObject node = (await CatalogAsync(services, docs, docs.McpEndpoint ?? mcpEndpoint, viewer)).AsObject();
        if (!includePresentation)
        {
            node.Remove("docs");
            foreach (string? section in new[] { "tools", "prompts", "resources", "resourceTemplates" })
            {
                if (node[section] is JsonArray entries)
                {
                    foreach (JsonObject entry in entries.OfType<JsonObject>())
                    {
                        entry.Remove("group");
                        entry.Remove("issues");
                    }
                }
            }
        }
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>
    /// The docs page as one self-contained HTML file, for GitHub Pages, for readers who can't reach the app, or for
    /// a stdio server, which has no page to map. Styles, script and catalog are inline and nothing is fetched, so it
    /// also works opened from disk. The output is stable: the same server and options give the same bytes.
    /// <para>
    /// The connect panel needs to know how a client reaches the server: pass <paramref name="mcpEndpoint"/> for
    /// Streamable HTTP, or <paramref name="stdioCommand"/> for a server the client starts itself. With neither,
    /// the page documents the primitives and leaves the connect panel out.
    /// </para>
    /// </summary>
    /// <param name="services">The app's services, e.g. <c>app.Services</c> or <c>host.Services</c>.</param>
    /// <param name="configure">The same configuration passed to <c>MapMcpDocs</c>: title, description, theme, links.</param>
    /// <param name="viewer">
    /// Who the page is for (anonymous when <see langword="null"/>). Matters only with
    /// <see cref="McpDocsGatedPrimitives.Hide"/>: the file then shows whatever this viewer may see to everyone who opens it.
    /// </param>
    /// <param name="mcpEndpoint">
    /// Absolute <c>http(s)</c> URL of the MCP endpoint, e.g. <c>https://api.example.com/mcp</c>. Defaults to
    /// <see cref="McpDocsOptions.McpEndpoint"/>, which must then be absolute too: a file has no host to resolve a path against.
    /// </param>
    /// <param name="stdioCommand">The command a client runs to start a stdio server, then its arguments, e.g. <c>["dnx", "Acme.Mcp", "--yes"]</c>.</param>
    /// <exception cref="ArgumentException">The endpoint isn't an absolute http(s) URL, the command is empty, or both are given.</exception>
    public static async Task<string> CreateHtmlAsync(
        IServiceProvider services,
        Action<McpDocsOptions>? configure = null,
        ClaimsPrincipal? viewer = null,
        string? mcpEndpoint = null,
        IReadOnlyList<string>? stdioCommand = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        McpDocsOptions docs = new McpDocsOptions();
        configure?.Invoke(docs);
        docs.Validate();

        string? endpoint = mcpEndpoint ?? docs.McpEndpoint;
        if (endpoint is not null && stdioCommand is not null)
        {
            throw new ArgumentException("Pass an MCP endpoint or a stdio command, not both: the page shows one way to connect.", nameof(stdioCommand));
        }

        if (endpoint is not null && !(Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
        {
            throw new ArgumentException(
                $"The MCP endpoint '{endpoint}' must be an absolute http(s) URL such as https://api.example.com/mcp: a static page has no host to resolve a path against.",
                nameof(mcpEndpoint));
        }

        if (stdioCommand is not null && (stdioCommand.Count == 0 || string.IsNullOrWhiteSpace(stdioCommand[0]) || stdioCommand.Any(a => a is null)))
        {
            throw new ArgumentException("The stdio command needs a program to run, then its arguments, e.g. [\"dnx\", \"Acme.Mcp\", \"--yes\"].", nameof(stdioCommand));
        }

        JsonObject catalog = (await CatalogAsync(services, docs, endpoint, viewer)).AsObject();
        McpDocsStaticPage.Prepare(catalog, endpoint, stdioCommand, origin: null);
        return McpDocsStaticPage.Render(docs, McpDocsAssets.Load(docs), catalog);
    }

    private static async Task<JsonNode> CatalogAsync(IServiceProvider services, McpDocsOptions docs, string? endpoint, ClaimsPrincipal? viewer)
    {
        McpServerOptions server = services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        McpDocsCatalog catalog = new McpDocsCatalog(server, docs, endpoint);

        using IServiceScope scope = services.CreateScope();
        DefaultHttpContext ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = viewer ?? new ClaimsPrincipal(new ClaimsIdentity()) };
        McpDocsCatalog.Result result = await catalog.GetAsync(ctx);
        return JsonNode.Parse(result.Json)!;
    }
}
