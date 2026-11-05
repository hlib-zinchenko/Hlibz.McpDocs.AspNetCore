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
