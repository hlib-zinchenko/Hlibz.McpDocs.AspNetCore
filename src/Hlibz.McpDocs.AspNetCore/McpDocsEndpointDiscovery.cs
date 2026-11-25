using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>Finds the route <c>MapMcp</c> mapped and checks it against <see cref="McpDocsOptions.McpEndpoint"/>.</summary>
internal static class McpDocsEndpointDiscovery
{
    private const string DefaultEndpoint = "/mcp";
    private const string McpDisplayNamePrefix = "MCP Streamable HTTP"; // the SDK names the endpoint it maps; it exposes no marker metadata

    /// <summary>The endpoint to publish in connect snippets (a path or an absolute URL), plus anything wrong with the setup.</summary>
    public sealed record Result(string Endpoint, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

    public static Result Resolve(IServiceProvider services, McpDocsOptions docs, string docsBasePath)
    {
        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        List<RouteEndpoint> routes = services.GetServices<EndpointDataSource>()
            .SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();
        List<RouteEndpoint> mcp = routes.Where(e => e.DisplayName?.StartsWith(McpDisplayNamePrefix, StringComparison.Ordinal) == true).ToList();
        List<string> mcpPaths = mcp.Select(e => Normalize(e.RoutePattern.RawText)).Distinct().ToList();

        string endpoint;
        if (docs.McpEndpoint is { } configured)
        {
            endpoint = configured;
            // An absolute URL points somewhere we can't check (gateway, other host).
            if (configured.StartsWith('/'))
            {
                string wanted = Normalize(configured);
                if (!routes.Any(e => Normalize(e.RoutePattern.RawText) == wanted))
                {
                    errors.Add($"McpDocsOptions.McpEndpoint is '{configured}' but no route with that path is mapped" +
                               (mcpPaths.Count > 0 ? $"; MapMcp is at {string.Join(", ", mcpPaths.Select(p => $"'{p}'"))}." : ". Is MapMcp called?"));
                }
            }
        }
        else if (mcpPaths.Count > 0)
        {
            endpoint = mcpPaths[0];
            if (mcpPaths.Count > 1)
            {
                warnings.Add($"MapMcp is mapped at several paths ({string.Join(", ", mcpPaths)}); the docs page uses '{endpoint}'. Set McpDocsOptions.McpEndpoint to choose.");
            }
        }
        else
        {
            endpoint = DefaultEndpoint;
            warnings.Add($"No MapMcp endpoint found; connect snippets use '{DefaultEndpoint}'. Set McpDocsOptions.McpEndpoint if the server is mapped elsewhere.");
        }

        // A gated MCP endpoint next to an open docs page publishes the whole surface to people the server turns away.
        AuthorizationPolicy? fallback = services.GetService<IOptions<AuthorizationOptions>>()?.Value.FallbackPolicy;
        if (fallback is null
            && mcp.Any(e => e.Metadata.GetMetadata<IAuthorizeData>() is not null)
            && !routes.Where(e => IsUnder(e, docsBasePath)).Any(e => e.Metadata.GetMetadata<IAuthorizeData>() is not null))
        {
            warnings.Add($"The MCP endpoint requires authorization but the docs page at '{docsBasePath}' does not. " +
                         "Call .RequireAuthorization() on MapMcpDocs, or accept that the page is public" +
                         (docs.GatedPrimitives == McpDocsGatedPrimitives.Hide
                             ? " ([Authorize] primitives are still filtered per viewer)."
                             : " ([Authorize] primitives are listed too, with what they require; GatedPrimitives = Hide filters them per viewer)."));
        }

        if (docs.TryIt is not null)
        {
            if (McpDocsTryItOptions.Unavailable(services) is { } reason)
            {
                warnings.Add($"EnableTryIt has no effect: {reason} Use SessionMode = Stateless (the default) or StatefulForInitializeClients.");
            }
            else if (!endpoint.StartsWith('/'))
            {
                warnings.Add($"The MCP endpoint is the absolute URL '{endpoint}'. The try-it console only calls an endpoint on the docs page's own origin, so it is hidden when that URL points elsewhere.");
            }
        }

        return new(endpoint, errors, warnings);
    }

    private static bool IsUnder(RouteEndpoint e, string basePath) =>
        Normalize(e.RoutePattern.RawText).StartsWith(basePath, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? path)
    {
        string p = "/" + (path ?? "").Trim('/');
        return p.ToLowerInvariant();
    }
}
