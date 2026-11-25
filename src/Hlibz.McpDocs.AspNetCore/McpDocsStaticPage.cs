using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>
/// The docs page as one self-contained HTML file: styles, script and catalog inline, nothing fetched. The served
/// page's CSP header becomes a <c>&lt;meta&gt;</c> policy that allows exactly the inline script and styles, by hash.
/// </summary>
internal static partial class McpDocsStaticPage
{
    [GeneratedRegex("</(?=script)", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptEnd();

    [GeneratedRegex("</(?=style)", RegexOptions.IgnoreCase)]
    private static partial Regex StyleEnd();

    /// <summary>
    /// Turns a served catalog into the one a static page carries: how to connect, and no paths into the app
    /// (made absolute with <paramref name="origin"/> when it's known, else dropped where they'd break).
    /// </summary>
    public static void Prepare(JsonObject catalog, string? endpoint, IReadOnlyList<string>? stdioCommand, string? origin)
    {
        JsonObject d = catalog["docs"]!.AsObject();
        d["static"] = true;
        if (endpoint is null)
        {
            d.Remove("mcpEndpoint");
        }
        else
        {
            d["mcpEndpoint"] = endpoint;
        }

        if (stdioCommand is not null)
        {
            d["stdio"] = new JsonObject
            {
                ["command"] = stdioCommand[0],
                ["args"] = new JsonArray(stdioCommand.Skip(1).Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
            };
        }

        // Favicon: a path into the app ("/x", already resolved from "~/x") needs the origin; without one the
        // server icon is used instead. Only https: and data: load under the file's CSP anyway.
        if ((string?)d["favicon"] is { } fav && fav.StartsWith('/'))
        {
            if (origin is null)
            {
                d.Remove("favicon");
            }
            else
            {
                d["favicon"] = origin + fav;
            }
        }

        // Toolbar links that are site paths would resolve against wherever the file is opened.
        if (origin is not null && d["links"] is JsonArray links)
        {
            foreach (JsonObject link in links.OfType<JsonObject>())
            {
                if ((string?)link["url"] is { } url && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal))
                {
                    link["url"] = origin + url;
                }
            }
        }
    }

    /// <summary>The page for the viewer of a served request, with the endpoint and links made absolute.</summary>
    public static string FromRequest(McpDocsExports.Source source, McpDocsOptions docs, McpDocsAssets assets, McpDocsColorMode mode)
    {
        JsonObject catalog = source.Catalog.AsObject(); // parsed for this render only
        Prepare(catalog, McpDocsExports.Endpoint(source), null, source.Origin);
        return Render(docs, assets, catalog, mode);
    }

    // mode: colour mode for first-time viewers of the file; defaults to McpDocsOptions.DefaultColorMode.
    public static string Render(McpDocsOptions docs, McpDocsAssets assets, JsonNode catalog, McpDocsColorMode? mode = null)
    {
        string title = (string?)catalog["docs"]?["title"] ?? "MCP server";

        // Same order of precedence as the served page: built-in styles, then the theme from the options.
        string css = Css(assets.Text("app.css")! + "\n" + (assets.Text("theme.css") ?? ""));
        string? print = assets.Text("print.css") is { } p ? Css(p) : null;
        string js = Script(assets.Text("app.js")!);

        // JSON only has '<' inside strings, where < means the same; no "</script" or "<!--" can survive.
        string json = catalog.ToJsonString().Replace("<", "\\u003c", StringComparison.Ordinal);

        string styleHashes = print is null ? Hash(css) : Hash(css) + " " + Hash(print);
        string csp = $"default-src 'none'; script-src {Hash(js)}; style-src {styleHashes}; img-src data: https:; base-uri 'none'; form-action 'none'";

        string theme = (mode ?? docs.DefaultColorMode) switch
        {
            McpDocsColorMode.Light => " data-theme=\"light\"",
            McpDocsColorMode.Dark => " data-theme=\"dark\"",
            _ => "",
        };

        StringBuilder sb = new StringBuilder(css.Length + js.Length + json.Length + 1024);
        sb.Append("<!doctype html>\n")
          .Append("<html lang=\"en\" data-static").Append(theme).Append(">\n")
          .Append("<head>\n")
          .Append("<meta charset=\"utf-8\">\n")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
          .Append("<meta name=\"color-scheme\" content=\"light dark\">\n")
          .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"").Append(csp).Append("\">\n")
          .Append("<meta name=\"referrer\" content=\"no-referrer\">\n")
          .Append("<title>").Append(WebUtility.HtmlEncode(title)).Append("</title>\n")
          .Append("<style>").Append(css).Append("</style>\n");
        if (print is not null)
        {
            sb.Append("<style media=\"print\">").Append(print).Append("</style>\n");
        }

        sb.Append("<script>").Append(js).Append("</script>\n")
          .Append("</head>\n")
          .Append("<body>\n")
          .Append("<div id=\"app\"><div class=\"boot\">Loading…</div></div>\n")
          .Append("<noscript><p class=\"boot\">This page needs JavaScript.</p></noscript>\n")
          .Append("<script type=\"application/json\" id=\"mcp-catalog\">").Append(json).Append("</script>\n")
          .Append("</body>\n")
          .Append("</html>\n");
        return sb.ToString();
    }

    // The HTML parser turns CRLF into LF before the browser hashes an inline block, so hash what it will see.
    private static string Normalize(string s) => s.TrimStart('\uFEFF').ReplaceLineEndings("\n");

    // "<\/style" means the same in CSS strings, and outside a string the text was invalid CSS anyway.
    private static string Css(string s) => StyleEnd().Replace(Normalize(s), "<\\/");

    // "<\/script" means the same in JS strings and regexes. "<!--" would put the HTML parser into a mode
    // where a later "<script" hides the closing tag; "<\!--" means the same in strings.
    private static string Script(string s) => ScriptEnd().Replace(Normalize(s), "<\\/").Replace("<!--", "<\\!--", StringComparison.Ordinal);

    private static string Hash(string inline) => "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(inline))) + "'";
}
