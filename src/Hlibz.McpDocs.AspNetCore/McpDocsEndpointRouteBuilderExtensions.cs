using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Hlibz.McpDocs.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Server;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps the MCP docs page.</summary>
public static class McpDocsEndpointRouteBuilderExtensions
{
    private static readonly string[] GetHead = [HttpMethods.Get, HttpMethods.Head];

    // Exports are cached per catalog variant, origin and PathBase. The origin comes from the Host header, so the
    // cache is capped rather than trusting it.
    private const int MaxCachedExports = 256;

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
        var exports = new ConcurrentDictionary<string, Asset>(StringComparer.Ordinal);

        var basePath = "/" + pattern.Trim('/');
        var docsPrefix = basePath == "/" ? "" : basePath; // so "{PathBase}{docsPrefix}/" never doubles the slash

        // Set once the app has started, if the setup is wrong. The app is stopped then; until it is, every docs
        // request fails with the same message instead of serving wrong connect snippets.
        string? setupError = null;
        void ThrowIfMisconfigured()
        {
            if (Volatile.Read(ref setupError) is { } e) throw new InvalidOperationException(e);
        }

        McpDocsCatalog GetCatalog(HttpContext ctx) => catalogs.GetOrAdd(ctx.Request.PathBase.Value ?? "", pb =>
        {
            var server = ctx.RequestServices.GetRequiredService<IOptions<McpServerOptions>>().Value;
            var found = McpDocsEndpointDiscovery.Resolve(ctx.RequestServices, docs, basePath).Endpoint;
            var endpoint = found.StartsWith('/') ? pb + found : found;
            return new McpDocsCatalog(server, docs, endpoint, pb);
        });

        var group = endpoints.MapGroup(pattern);

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
            Volatile.Write(ref setupError, "MCP docs are misconfigured: " + string.Join(" ", check.Errors));
            lifetime.StopApplication();
        });

        // "/mcp-docs" → "/mcp-docs/" so relative asset URLs resolve.
        group.MapMethods("", GetHead, ctx =>
        {
            ThrowIfMisconfigured();
            if (ctx.Request.Path.Value?.EndsWith('/') == true) return Page(ctx, docs, assets);
            ctx.Response.Redirect(ctx.Request.PathBase + docsPrefix + "/" + ctx.Request.QueryString);
            return Task.CompletedTask;
        }).ExcludeFromDescription(); // routing ignores the trailing slash, so this one route serves both forms

        group.MapMethods("/catalog.json", GetHead, async ctx =>
        {
            ThrowIfMisconfigured();
            // Content depends on the viewer once anything is [Authorize]d: never let a shared cache keep it.
            var result = await GetCatalog(ctx).GetAsync(ctx);
            await Send(ctx, result.Json, "application/json; charset=utf-8", result.ETag, "private, no-cache");
        }).ExcludeFromDescription();

        // Exports derived from the catalog, so they list exactly what this viewer's catalog.json does. Rendered
        // once per catalog variant, origin and PathBase; a repeat request is a lookup.
        async Task Export(HttpContext ctx, string file, string contentType, Func<McpDocsExports.Source, string> render)
        {
            ThrowIfMisconfigured();
            var result = await GetCatalog(ctx).GetAsync(ctx);
            var req = ctx.Request;
            var origin = $"{req.Scheme}://{req.Host}";
            var pathBase = req.PathBase.Value ?? "";
            var key = string.Join('\n', file, result.ETag, origin, pathBase);
            if (!exports.TryGetValue(key, out var export))
            {
                var body = Encoding.UTF8.GetBytes(render(new McpDocsExports.Source(JsonNode.Parse(result.Json)!, result.ETag, origin, pathBase, basePath)));
                export = new Asset(body, contentType, McpDocsCatalog.ETagOf(body), "");
                if (exports.Count < MaxCachedExports) exports.TryAdd(key, export);
            }
            await Send(ctx, export.Bytes, contentType, export.ETag, "private, no-cache");
        }

        group.MapMethods("/catalog.md", GetHead, ctx => Export(ctx, "catalog.md", "text/markdown; charset=utf-8", McpDocsExports.Markdown))
            .ExcludeFromDescription();

        // llms.txt convention, scoped to the docs group: a short index that links to catalog.md.
        group.MapMethods("/llms.txt", GetHead, ctx => Export(ctx, "llms.txt", "text/plain; charset=utf-8", McpDocsExports.LlmsTxt))
            .ExcludeFromDescription();

        if (docs.RegistryName is { } registryName)
            group.MapMethods("/server.json", GetHead, ctx => Export(ctx, "server.json", "application/json; charset=utf-8", s => McpDocsExports.ServerJson(s, registryName)))
                .ExcludeFromDescription();

        group.MapMethods("/_assets/{file}", GetHead, ctx =>
        {
            if (ctx.Request.RouteValues["file"] is string file && assets.Files.TryGetValue(file, out var a))
                return Send(ctx, a.Bytes, a.ContentType, a.ETag, "public, max-age=31536000, immutable");
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }).ExcludeFromDescription();

        return group;
    }

    private static Task Page(HttpContext ctx, McpDocsOptions docs, Assets assets)
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
        return Write(ctx, Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8");
    }

    /// <summary>Writes <paramref name="body"/> with its validator, or a 304 when the client already has it.</summary>
    private static Task Send(HttpContext ctx, byte[] body, string contentType, string etag, string cacheControl)
    {
        var res = ctx.Response;
        res.Headers.ETag = etag;
        res.Headers.CacheControl = cacheControl;
        res.Headers.XContentTypeOptions = "nosniff";
        if (NoneMatch(ctx.Request.Headers.IfNoneMatch, etag))
        {
            res.StatusCode = StatusCodes.Status304NotModified;
            return Task.CompletedTask;
        }
        return Write(ctx, body, contentType);
    }

    /// <summary>Headers always; the body only for GET (a HEAD response carries the length it would have).</summary>
    private static Task Write(HttpContext ctx, byte[] body, string contentType)
    {
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength = body.Length;
        return HttpMethods.IsHead(ctx.Request.Method) ? Task.CompletedTask : ctx.Response.Body.WriteAsync(body).AsTask();
    }

    /// <summary>
    /// <c>If-None-Match</c> with the weak comparison RFC 9110 prescribes for it: <c>W/"x"</c> matches <c>"x"</c>
    /// (proxies that compress often weaken a strong ETag), and <c>*</c> matches anything.
    /// </summary>
    internal static bool NoneMatch(StringValues header, string etag)
    {
        foreach (var value in header)
        {
            if (value is null) continue;
            foreach (var part in value.Split(','))
            {
                var tag = part.Trim();
                if (tag == "*") return true;
                if (tag.StartsWith("W/", StringComparison.Ordinal)) tag = tag[2..];
                if (tag == etag) return true;
            }
        }
        return false;
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
