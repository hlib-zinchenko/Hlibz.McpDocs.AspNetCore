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
    /// Maps a docs page for the MCP server registered in DI:
    /// <c>{pattern}/</c> (page), <c>{pattern}/catalog.json</c> and <c>{pattern}/_assets/*</c>.
    /// Returns the route group so <c>.RequireAuthorization()</c> etc. cover the page, the JSON and the assets.
    /// </summary>
    public static RouteGroupBuilder MapMcpDocs(this IEndpointRouteBuilder endpoints, string pattern = "/mcp-docs", Action<McpDocsOptions>? configure = null)
    {
        McpDocsOptions docs = new McpDocsOptions();
        configure?.Invoke(docs);
        docs.Validate();

        McpDocsAssets assets = McpDocsAssets.Load(docs);
        ConcurrentDictionary<string, McpDocsCatalog> catalogs = new ConcurrentDictionary<string, McpDocsCatalog>(); // keyed by PathBase
        ConcurrentDictionary<string, McpDocsAsset> exports = new ConcurrentDictionary<string, McpDocsAsset>(StringComparer.Ordinal);

        string basePath = "/" + pattern.Trim('/');
        string docsPrefix = basePath == "/" ? "" : basePath; // so "{PathBase}{docsPrefix}/" never doubles the slash

        // Set once the app has started, if the setup is wrong. The app is stopped then; until it is, every docs
        // request fails with the same message instead of serving wrong connect snippets.
        string? setupError = null;
        void ThrowIfMisconfigured()
        {
            if (Volatile.Read(ref setupError) is { } e)
            {
                throw new InvalidOperationException(e);
            }
        }

        McpDocsCatalog GetCatalog(HttpContext ctx) => catalogs.GetOrAdd(ctx.Request.PathBase.Value ?? "", pb =>
        {
            McpServerOptions server = ctx.RequestServices.GetRequiredService<IOptions<McpServerOptions>>().Value;
            string found = McpDocsEndpointDiscovery.Resolve(ctx.RequestServices, docs, basePath).Endpoint;
            string endpoint = found.StartsWith('/') ? pb + found : found;
            return new McpDocsCatalog(server, docs, endpoint, pb, McpDocsTryItOptions.Unavailable(ctx.RequestServices));
        });

        RouteGroupBuilder group = endpoints.MapGroup(pattern);

        // Endpoints are only complete once the app has built its pipeline, so check when it starts.
        IServiceProvider services = endpoints.ServiceProvider;
        IHostApplicationLifetime lifetime = services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() =>
        {
            ILogger log = services.GetRequiredService<ILoggerFactory>().CreateLogger("Hlibz.McpDocs.AspNetCore");
            McpDocsEndpointDiscovery.Result check = McpDocsEndpointDiscovery.Resolve(services, docs, basePath);
            foreach (string w in check.Warnings)
            {
                log.LogWarning("{Message}", w);
            }

            if (docs.CheckDescriptions && services.GetService<IOptions<McpServerOptions>>()?.Value is { } server)
            {
                foreach (string w in McpDocsQuality.Warnings(server))
                {
                    log.LogWarning("{Message}", w);
                }
            }

            if (check.Errors.Count == 0)
            {
                return;
            }

            foreach (string e in check.Errors)
            {
                log.LogCritical("{Message}", e);
            }

            Volatile.Write(ref setupError, "MCP docs are misconfigured: " + string.Join(" ", check.Errors));
            lifetime.StopApplication();
        });

        // "/mcp-docs" → "/mcp-docs/" so relative asset URLs resolve.
        group.MapMethods("", GetHead, ctx =>
        {
            ThrowIfMisconfigured();
            if (ctx.Request.Path.Value?.EndsWith('/') == true)
            {
                return Page(ctx, docs, assets);
            }

            ctx.Response.Redirect(ctx.Request.PathBase + docsPrefix + "/" + ctx.Request.QueryString);
            return Task.CompletedTask;
        }).ExcludeFromDescription(); // routing ignores the trailing slash, so this one route serves both forms

        group.MapMethods("/catalog.json", GetHead, async ctx =>
        {
            ThrowIfMisconfigured();
            // Content depends on the viewer once anything is [Authorize]d: never let a shared cache keep it.
            McpDocsCatalog.Result result = await GetCatalog(ctx).GetAsync(ctx);
            await Send(ctx, result.Json, "application/json; charset=utf-8", result.ETag, "private, no-cache");
        }).ExcludeFromDescription();

        // Exports derived from the catalog, so they list exactly what this viewer's catalog.json does. Rendered
        // once per catalog variant, origin and PathBase; a repeat request is a lookup.
        async Task Export(HttpContext ctx, string file, string contentType, Func<McpDocsExports.Source, string> render, string? attachment = null)
        {
            ThrowIfMisconfigured();
            McpDocsCatalog.Result result = await GetCatalog(ctx).GetAsync(ctx);
            HttpRequest req = ctx.Request;
            string origin = $"{req.Scheme}://{req.Host}";
            string pathBase = req.PathBase.Value ?? "";
            string key = string.Join('\n', file, result.ETag, origin, pathBase);
            if (!exports.TryGetValue(key, out McpDocsAsset? export))
            {
                byte[] body = Encoding.UTF8.GetBytes(render(new McpDocsExports.Source(JsonNode.Parse(result.Json)!, result.ETag, origin, pathBase, basePath)));
                export = new McpDocsAsset(body, contentType, McpDocsCatalog.ETagOf(body), "");
                if (exports.Count < MaxCachedExports)
                {
                    exports.TryAdd(key, export);
                }
            }
            if (attachment is not null)
            {
                ContentDispositionHeaderValue disposition = new ContentDispositionHeaderValue("attachment");
                disposition.SetHttpFileName(attachment);
                ctx.Response.Headers.ContentDisposition = disposition.ToString();
            }
            await Send(ctx, export.Bytes, contentType, export.ETag, "private, no-cache");
        }

        group.MapMethods("/catalog.md", GetHead, ctx => Export(ctx, "catalog.md", "text/markdown; charset=utf-8", McpDocsExports.Markdown))
            .ExcludeFromDescription();

        // llms.txt convention, scoped to the docs group: a short index that links to catalog.md.
        group.MapMethods("/llms.txt", GetHead, ctx => Export(ctx, "llms.txt", "text/plain; charset=utf-8", McpDocsExports.LlmsTxt))
            .ExcludeFromDescription();

        if (docs.RegistryName is { } registryName)
        {
            group.MapMethods("/server.json", GetHead, ctx => Export(ctx, "server.json", "application/json; charset=utf-8", s => McpDocsExports.ServerJson(s, registryName)))
                .ExcludeFromDescription();
        }

        // The page as one self-contained file, as this viewer sees it: the download button's target.
        if (docs.HtmlExport)
        {
            group.MapMethods("/docs.html", GetHead, ctx =>
            {
                McpServerOptions server = ctx.RequestServices.GetRequiredService<IOptions<McpServerOptions>>().Value;
                string key = docs.ServerKey ?? McpDocsCatalog.Slug(server.ServerInfo?.Name ?? "mcp-server");
                // ?theme=light|dark|system: the viewer's mode on the page, so the file opens the way they saw it.
                McpDocsColorMode mode = (string?)ctx.Request.Query["theme"] switch
                {
                    "light" => McpDocsColorMode.Light,
                    "dark" => McpDocsColorMode.Dark,
                    "system" => McpDocsColorMode.System,
                    _ => docs.DefaultColorMode,
                };
                return Export(ctx, "docs.html\n" + mode, "text/html; charset=utf-8", s => McpDocsStaticPage.FromRequest(s, docs, assets, mode),
                    attachment: (key.Length > 0 ? key : "mcp-server") + "-docs.html");
            }).ExcludeFromDescription();
        }

        group.MapMethods("/_assets/{file}", GetHead, ctx =>
        {
            if (ctx.Request.RouteValues["file"] is string file && assets.Files.TryGetValue(file, out McpDocsAsset? a))
            {
                return Send(ctx, a.Bytes, a.ContentType, a.ETag, "public, max-age=31536000, immutable");
            }

            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }).ExcludeFromDescription();

        return group;
    }

    private static Task Page(HttpContext ctx, McpDocsOptions docs, McpDocsAssets assets)
    {
        string title = WebUtility.HtmlEncode(docs.Title ?? ctx.RequestServices.GetService<IOptions<McpServerOptions>>()?.Value.ServerInfo?.Title ?? "MCP server");
        string html = assets.PageTemplate
            .Replace("{{title}}", title)
            .Replace("{{download}}", docs.HtmlExport ? " data-download=\"docs.html\"" : "")
            .Replace("{{mode}}", docs.DefaultColorMode switch { McpDocsColorMode.Light => " data-theme=\"light\"", McpDocsColorMode.Dark => " data-theme=\"dark\"", _ => "" })
            .Replace("{{theme}}", assets.Files.TryGetValue("theme.css", out McpDocsAsset? theme) ? $"<link rel=\"stylesheet\" href=\"_assets/theme.css?v={theme.Version}\">" : "")
            .Replace("{{print}}", assets.Files.TryGetValue("print.css", out McpDocsAsset? print) ? $"<link rel=\"stylesheet\" media=\"print\" href=\"_assets/print.css?v={print.Version}\">" : "")
            .Replace("{{css}}", "_assets/app.css?v=" + assets.Files["app.css"].Version)
            .Replace("{{js}}", "_assets/app.js?v=" + assets.Files["app.js"].Version);

        IHeaderDictionary h = ctx.Response.Headers;
        // Ours is a default: an app with its own CSP middleware has already set the header, and wins.
        if (!h.ContainsKey(HeaderNames.ContentSecurityPolicy))
        {
            h.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data: https:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        }

        h.XContentTypeOptions = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        h.CacheControl = "no-cache";
        return Write(ctx, Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8");
    }

    /// <summary>Writes <paramref name="body"/> with its validator, or a 304 when the client already has it.</summary>
    private static Task Send(HttpContext ctx, byte[] body, string contentType, string etag, string cacheControl)
    {
        HttpResponse res = ctx.Response;
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
        foreach (string? value in header)
        {
            if (value is null)
            {
                continue;
            }

            foreach (string part in value.Split(','))
            {
                string tag = part.Trim();
                if (tag == "*")
                {
                    return true;
                }

                if (tag.StartsWith("W/", StringComparison.Ordinal))
                {
                    tag = tag[2..];
                }

                if (tag == etag)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
