using System.Text;
using System.Text.RegularExpressions;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>Builds <c>theme.css</c> from the options and validates the values that end up in it.</summary>
internal static partial class McpDocsTheme
{
    [GeneratedRegex(@"^(#[0-9a-fA-F]{3,8}|(rgb|hsl)a?\([0-9.,%\s/a-z]{1,60}\)|[a-zA-Z]{3,24})$")]
    private static partial Regex ColorRx();

    public static bool IsSafeColor(string v) => ColorRx().IsMatch(v);

    public static bool IsSafeLink(string v) =>
        v.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || v.StartsWith("~/", StringComparison.Ordinal) || (v.StartsWith('/') && !v.StartsWith("//", StringComparison.Ordinal));

    public static bool IsSafeFavicon(string v) =>
        v.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
        || v.StartsWith("~/", StringComparison.Ordinal) || (v.StartsWith('/') && !v.StartsWith("//", StringComparison.Ordinal));

    /// <summary>"~/x" → "{pathBase}/x"; everything else unchanged.</summary>
    public static string Resolve(string url, string pathBase) => url.StartsWith("~/", StringComparison.Ordinal) ? pathBase + "/" + url[2..] : url;

    // Dark palettes: bg, sidebar, surface, text, muted, faint, border, accent, panel, panel-2, panel-border.
    private static string? Preset(McpDocsDarkPreset p) => p switch
    {
        McpDocsDarkPreset.Midnight => "--bg:#0b1020;--sidebar:#0e1428;--surface:#131a33;--text:#e6e9f5;--muted:#9aa3c4;--faint:#8892b8;--border:#1f2848;--accent:#7aa2ff;--panel:#070b17;--panel-2:#0e1428;--panel-border:#1b2442;",
        McpDocsDarkPreset.Nord => "--bg:#2e3440;--sidebar:#292e39;--surface:#3b4252;--text:#eceff4;--muted:#a9b3c6;--faint:#a8b3c8;--border:#434c5e;--accent:#88c0d0;--panel:#242933;--panel-2:#2e3440;--panel-border:#3b4252;",
        McpDocsDarkPreset.GitHubDark => "--bg:#0d1117;--sidebar:#010409;--surface:#161b22;--text:#e6edf3;--muted:#9198a1;--faint:#8b949e;--border:#30363d;--accent:#4493f8;--panel:#010409;--panel-2:#0d1117;--panel-border:#30363d;",
        _ => null,
    };

    private static string Accent(string color, int percent) =>
        $"--accent:{color};--accent-soft:color-mix(in srgb,{color} {percent}%,transparent);";

    public static string Build(McpDocsOptions o)
    {
        var sb = new StringBuilder();
        if (o.PrimaryColor is { } light) sb.Append(":root{").Append(Accent(light, 9)).Append("}\n");

        var dark = new StringBuilder(Preset(o.DarkPreset));
        var darkColor = o.DarkModePrimaryColor ?? o.PrimaryColor;
        if (darkColor is not null) dark.Append(Accent(darkColor, 12));
        else if (o.DarkPreset != McpDocsDarkPreset.Default) dark.Append("--accent-soft:color-mix(in srgb,var(--accent) 12%,transparent);");
        if (dark.Length > 0)
        {
            sb.Append("@media (prefers-color-scheme:dark){:root:not([data-theme=\"light\"]){").Append(dark).Append("}}\n");
            sb.Append(":root[data-theme=\"dark\"]{").Append(dark).Append("}\n");
        }

        if (!string.IsNullOrWhiteSpace(o.AdditionalCss)) sb.Append(o.AdditionalCss).Append('\n');
        return sb.ToString();
    }
}
