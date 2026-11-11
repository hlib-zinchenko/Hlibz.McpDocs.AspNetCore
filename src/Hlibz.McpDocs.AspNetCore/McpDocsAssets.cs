using System.Text;

namespace Hlibz.McpDocs.AspNetCore;

internal sealed record McpDocsAsset(byte[] Bytes, string ContentType, string ETag, string Version);

/// <summary>The embedded page template, script and styles, plus the theme built from the options.</summary>
internal sealed class McpDocsAssets
{
    public required string PageTemplate { get; init; }
    public required Dictionary<string, McpDocsAsset> Files { get; init; }

    public static McpDocsAssets Load(McpDocsOptions docs) =>
        Load(Encoding.UTF8.GetBytes(McpDocsTheme.Build(docs)), docs.PrintStyles);

    private static McpDocsAssets Load(byte[] theme, bool print)
    {
        var files = new Dictionary<string, McpDocsAsset>
        {
            ["app.css"] = Make(Read("app.css"), "text/css; charset=utf-8"),
            ["app.js"] = Make(Read("app.js"), "text/javascript; charset=utf-8"),
        };
        if (print) files["print.css"] = Make(Read("print.css"), "text/css; charset=utf-8");
        if (theme.Length > 0) files["theme.css"] = Make(theme, "text/css; charset=utf-8");
        return new McpDocsAssets { PageTemplate = Encoding.UTF8.GetString(Read("index.html")), Files = files };
    }

    /// <summary>The file as text, or <see langword="null"/> when it isn't part of this configuration.</summary>
    public string? Text(string file) => Files.TryGetValue(file, out var a) ? Encoding.UTF8.GetString(a.Bytes) : null;

    private static McpDocsAsset Make(byte[] bytes, string type)
    {
        var etag = McpDocsCatalog.ETagOf(bytes);
        return new McpDocsAsset(bytes, type, etag, etag.Trim('"')[..10]);
    }

    private static byte[] Read(string name)
    {
        using var s = typeof(McpDocsAssets).Assembly.GetManifestResourceStream("McpDocs." + name)
            ?? throw new InvalidOperationException($"Embedded asset '{name}' missing.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
