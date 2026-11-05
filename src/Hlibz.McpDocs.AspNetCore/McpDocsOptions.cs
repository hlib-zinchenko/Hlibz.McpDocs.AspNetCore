using ModelContextProtocol.Server;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>Options for the MCP docs page.</summary>
public sealed class McpDocsOptions
{
    /// <summary>Page title. Defaults to the server's title or name from <see cref="McpServerOptions.ServerInfo"/>.</summary>
    public string? Title { get; set; }

    /// <summary>Intro shown under the title. Markdown subset. Defaults to the server instructions.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Path of the MCP endpoint (as passed to <c>MapMcp</c>), or an absolute URL. Used in the connect snippets.
    /// Defaults to the route <c>MapMcp</c> mapped. A path that matches no mapped route stops the app at startup.
    /// </summary>
    public string? McpEndpoint { get; set; }

    /// <summary>Server key used in the generated client configs, e.g. <c>"acme-orders"</c>. Defaults to the server name.</summary>
    public string? ServerKey { get; set; }

    /// <summary>When set, connect snippets include this header with <see cref="AuthPlaceholder"/>, e.g. <c>Authorization</c>.</summary>
    public string? AuthHeader { get; set; }

    /// <summary>Placeholder value for <see cref="AuthHeader"/>.</summary>
    public string AuthPlaceholder { get; set; } = "Bearer <token>";

    /// <summary>Free text telling the reader how to get credentials. Markdown subset.</summary>
    public string? AuthHint { get; set; }

    /// <summary>
    /// Show each primitive's <c>_meta</c> on the page. Off by default: it is often implementation detail,
    /// and when off it is also left out of <c>catalog.json</c>.
    /// </summary>
    public bool ShowMeta { get; set; }

    /// <summary>
    /// Check descriptions: tools, prompts and resources without one, parameters and arguments without one, tools
    /// without annotations. Shown on the page and logged as warnings at startup. On by default; to keep the page
    /// out of an environment, don't map it there.
    /// </summary>
    public bool CheckDescriptions { get; set; } = true;

    /// <summary>Colour mode for first-time viewers. A viewer's own choice (the toggle) wins afterwards.</summary>
    public McpDocsColorMode DefaultColorMode { get; set; } = McpDocsColorMode.System;

    /// <summary>
    /// Accent colour: <c>#rgb</c>, <c>#rrggbb</c>, <c>rgb()</c>/<c>hsl()</c> or a colour name. Anything else
    /// throws when the page is mapped, so a value can't break out of the CSS declaration.
    /// </summary>
    public string? PrimaryColor { get; set; }

    /// <summary>Accent colour in dark mode. Defaults to <see cref="PrimaryColor"/>, else the dark preset's own.</summary>
    public string? DarkModePrimaryColor { get; set; }

    /// <summary>Palette used in dark mode.</summary>
    public McpDocsDarkPreset DarkPreset { get; set; } = McpDocsDarkPreset.Default;

    /// <summary>
    /// CSS appended after the built-in stylesheet. Served as a file (<c>_assets/theme.css</c>), not inline,
    /// so the page's CSP keeps holding.
    /// </summary>
    public string? AdditionalCss { get; set; }

    /// <summary>
    /// Favicon: an <c>https:</c> URL, a <c>data:image/</c> URI, or a path (<c>~/</c> resolves against the
    /// PathBase). Defaults to the server's icon.
    /// </summary>
    public string? Favicon { get; set; }

    internal List<McpDocsToolbarItem> ToolbarItems { get; } = [];

    /// <summary>
    /// Adds a link to the sidebar, e.g. a changelog or status page. <paramref name="url"/> is <c>http(s)://</c>,
    /// or a path (<c>~/</c> resolves against the PathBase). A link and nothing else: no HTML or script.
    /// </summary>
    public McpDocsOptions AddToolbarItem(string label, string url, bool newTab = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (!McpDocsTheme.IsSafeLink(url))
            throw new ArgumentException($"Toolbar URL '{url}' must start with http://, https://, ~/ or /.", nameof(url));
        ToolbarItems.Add(new(label.Trim(), url.Trim(), newTab));
        return this;
    }

    internal void Validate()
    {
        foreach (var (name, value) in new[] { (nameof(PrimaryColor), PrimaryColor), (nameof(DarkModePrimaryColor), DarkModePrimaryColor) })
            if (value is not null && !McpDocsTheme.IsSafeColor(value))
                throw new ArgumentException($"{name} '{value}' is not a hex, rgb(), hsl() or named colour.");
        if (Favicon is not null && !McpDocsTheme.IsSafeFavicon(Favicon))
            throw new ArgumentException($"Favicon '{Favicon}' must start with https://, data:image/, ~/ or /.");
    }

    /// <summary>
    /// Assigns a sidebar group to each tool; <c>null</c> = ungrouped.
    /// Defaults to <see cref="McpDocsGrouping.ByAttribute"/> (reads <see cref="McpDocsGroupAttribute"/>).
    /// </summary>
    public Func<McpServerTool, string?> ToolGroup { get; set; } = McpDocsGrouping.ByAttribute;
}

/// <summary>
/// Puts a tool into a sidebar group on the docs page. Apply to the tool class (all its tools)
/// or to a single tool method (wins over the class).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class McpDocsGroupAttribute(string name) : Attribute
{
    /// <summary>Group name shown in the sidebar.</summary>
    public string Name { get; } = name;
}

/// <summary>Built-in tool grouping strategies.</summary>
public static class McpDocsGrouping
{
    /// <summary>No groups.</summary>
    public static string? None(McpServerTool tool) => null;

    /// <summary>
    /// Uses <see cref="McpDocsGroupAttribute"/> from the tool's metadata. The SDK puts class attributes
    /// before method attributes, so the last one is the most specific.
    /// </summary>
    public static string? ByAttribute(McpServerTool tool) =>
        tool.Metadata.OfType<McpDocsGroupAttribute>().LastOrDefault()?.Name;

    /// <summary>Groups by the prefix before the first <c>_</c>, <c>.</c> or <c>-</c> in the tool name.</summary>
    public static string? ByNamePrefix(McpServerTool tool)
    {
        var name = tool.ProtocolTool.Name;
        var i = name.IndexOfAny(['_', '.', '-']);
        return i > 0 ? name[..i] : null;
    }
}

/// <summary>Colour mode for first-time viewers.</summary>
public enum McpDocsColorMode
{
    /// <summary>Follow the operating system.</summary>
    System,
    /// <summary>Light.</summary>
    Light,
    /// <summary>Dark.</summary>
    Dark,
}

/// <summary>Dark palettes.</summary>
public enum McpDocsDarkPreset
{
    /// <summary>The built-in neutral dark.</summary>
    Default,
    /// <summary>Deep blue-black.</summary>
    Midnight,
    /// <summary>Nord.</summary>
    Nord,
    /// <summary>GitHub's dark theme.</summary>
    GitHubDark,
}

internal sealed record McpDocsToolbarItem(string Label, string Url, bool NewTab);
