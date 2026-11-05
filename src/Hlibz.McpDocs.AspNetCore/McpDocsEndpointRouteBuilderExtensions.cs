using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using Hlibz.McpDocs.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Server;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps the MCP docs page.</summary>
public static class McpDocsEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a read-only docs page for the MCP server registered in DI:
    /// <c>{pattern}/</c> (page), <c>{pattern}/catalog.json</c> and <c>{pattern}/_assets/*</c>.
    /// Returns the route group so <c>.RequireAuthorization()</c> etc. cover the page, the JSON and the assets.
    /// </summary>
    public static RouteGroupBuilder MapMcpDocs(this IEndpointRouteBuilder endpoints, string pattern = "/mcp-docs", Action<McpDocsOptions>? configure = null)
    {
        var docs = new McpDocsOptions();
        configure?.Invoke(docs);
        docs.Validate();

        var assets = Assets.Load(Encoding.UTF8.GetBytes(McpDocsTheme.Build(docs)), docs.PrintStyles);
        var catalogs = new ConcurrentDictionary<string, McpDocsCatalog>(); // keyed by PathBase

        var group = endpoints.MapGroup(pattern);
        var basePath = "/" + pattern.Trim('/');

        // Endpoints are only complete once the app has built its pipeline, so check when it starts.
        var services = endpoints.ServiceProvider;
        var lifetime = services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() =>
        {
            var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("Hlibz.McpDocs.AspNetCore");
            var check = McpDocsEndpointDiscovery.Resolve(services, docs, basePath);
            foreach (var w in check.Warnings) log.LogWarning("{Message}", w);
            if (docs.CheckDescriptions && services.GetService<IOptions<McpServerOptions>>()?.Value is { } server)
                foreach (var w in McpDocsQuality.Warnings(server)) log.LogWarning("{Message}", w);
            if (check.Errors.Count == 0) return;
            foreach (var e in check.Errors) log.LogCritical("{Message}", e);
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        });

        // "/mcp-docs" → "/mcp-docs/" so relative asset URLs resolve.
        group.MapGet("", ctx =>
            (ctx.Request.Path.Value!.EndsWith('/')
                ? Page(ctx, docs, assets)
                : Results.Redirect(ctx.Request.PathBase + basePath + "/" + ctx.Request.QueryString)).ExecuteAsync(ctx))
            .ExcludeFromDescription(); // routing ignores the trailing slash, so this one route serves both forms

        group.MapGet("/catalog.json", async ctx =>
        {
            var pathBase = ctx.Request.PathBase.Value ?? "";
            var catalog = catalogs.GetOrAdd(pathBase, pb =>
            {
                var server = ctx.RequestServices.GetRequiredService<IOptions<McpServerOptions>>().Value;
                var found = McpDocsEndpointDiscovery.Resolve(ctx.RequestServices, docs, basePath).Endpoint;
                var endpoint = found.StartsWith('/') ? pb + found : found;
                return new McpDocsCatalog(server, docs, endpoint, pb);
            });
            // Content depends on the viewer once anything is [Authorize]d: never let a shared cache keep it.
            var result = await catalog.GetAsync(ctx);
            await Cached(ctx, result.Json, "application/json; charset=utf-8", result.ETag, "private, no-cache").ExecuteAsync(ctx);
        }).ExcludeFromDescription();

        group.MapGet("/_assets/{file}", ctx =>
            (ctx.Request.RouteValues["file"] is string file && assets.Files.TryGetValue(file, out var a)
                ? Cached(ctx, a.Bytes, a.ContentType, a.ETag, "public, max-age=31536000, immutable")
                : Results.NotFound()).ExecuteAsync(ctx))
            .ExcludeFromDescription();

        return group;
    }

    private static IResult Page(HttpContext ctx, McpDocsOptions docs, Assets assets)
    {
        var title = WebUtility.HtmlEncode(docs.Title ?? ctx.RequestServices.GetService<IOptions<McpServerOptions>>()?.Value.ServerInfo?.Title ?? "MCP server");
        var html = assets.PageTemplate
            .Replace("{{title}}", title)
            .Replace("{{mode}}", docs.DefaultColorMode switch { McpDocsColorMode.Light => " data-theme=\"light\"", McpDocsColorMode.Dark => " data-theme=\"dark\"", _ => "" })
            .Replace("{{theme}}", assets.Files.TryGetValue("theme.css", out var theme) ? $"<link rel=\"stylesheet\" href=\"_assets/theme.css?v={theme.Version}\">" : "")
            .Replace("{{print}}", assets.Files.TryGetValue("print.css", out var print) ? $"<link rel=\"stylesheet\" media=\"print\" href=\"_assets/print.css?v={print.Version}\">" : "")
            .Replace("{{css}}", "_assets/app.css?v=" + assets.Files["app.css"].Version)
            .Replace("{{js}}", "_assets/app.js?v=" + assets.Files["app.js"].Version);

        var h = ctx.Response.Headers;
        // Ours is a default: an app with its own CSP middleware has already set the header, and wins.
        if (!h.ContainsKey(HeaderNames.ContentSecurityPolicy))
            h.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data: https:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        h.XContentTypeOptions = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        h.CacheControl = "no-cache";
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static IResult Cached(HttpContext ctx, byte[] body, string contentType, string etag, string cacheControl)
    {
        var res = ctx.Response;
        res.Headers.ETag = etag;
        res.Headers.CacheControl = cacheControl;
        res.Headers.XContentTypeOptions = "nosniff";
        if (ctx.Request.Headers.IfNoneMatch.Any(v => v is not null && v.Split(',').Any(x => x.Trim() == etag)))
            return Results.StatusCode(StatusCodes.Status304NotModified);
        return Results.Bytes(body, contentType);
    }

    private sealed record Asset(byte[] Bytes, string ContentType, string ETag, string Version);

    private sealed class Assets
    {
        public required string PageTemplate { get; init; }
        public required Dictionary<string, Asset> Files { get; init; }

        public static Assets Load(byte[] theme, bool print)
        {
            var files = new Dictionary<string, Asset>
            {
                ["app.css"] = Make(Read("app.css"), "text/css; charset=utf-8"),
                ["app.js"] = Make(Read("app.js"), "text/javascript; charset=utf-8"),
            };
            if (print) files["print.css"] = Make(Read("print.css"), "text/css; charset=utf-8");
            if (theme.Length > 0) files["theme.css"] = Make(theme, "text/css; charset=utf-8");
            return new Assets { PageTemplate = Encoding.UTF8.GetString(Read("index.html")), Files = files };
        }

        private static Asset Make(byte[] bytes, string type)
        {
            var etag = McpDocsCatalog.ETagOf(bytes);
            return new Asset(bytes, type, etag, etag.Trim('"')[..10]);
        }

        private static byte[] Read(string name)
        {
            using var s = typeof(Assets).Assembly.GetManifestResourceStream("McpDocs." + name)
                ?? throw new InvalidOperationException($"Embedded asset '{name}' missing.");
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
