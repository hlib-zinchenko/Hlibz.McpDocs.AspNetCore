using System.Text.Json;
using ModelContextProtocol.Server;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>Description checks. The description is the interface a model sees, so a missing one is a bug.</summary>
internal static class McpDocsQuality
{
    public static List<string> Check(IMcpServerPrimitive primitive)
    {
        var issues = new List<string>();
        switch (primitive)
        {
            case McpServerTool { ProtocolTool: var t }:
                if (string.IsNullOrWhiteSpace(t.Description)) issues.Add("No description.");
                if (t.Annotations is null) issues.Add("No annotations (readOnly / destructive / idempotent / openWorld hints).");
                var missing = UndescribedProperties(t.InputSchema);
                if (missing.Count > 0) issues.Add("Parameters without a description: " + string.Join(", ", missing) + ".");
                break;
            case McpServerPrompt { ProtocolPrompt: var p }:
                if (string.IsNullOrWhiteSpace(p.Description)) issues.Add("No description.");
                var args = p.Arguments?.Where(a => string.IsNullOrWhiteSpace(a.Description)).Select(a => a.Name).ToList() ?? [];
                if (args.Count > 0) issues.Add("Arguments without a description: " + string.Join(", ", args) + ".");
                break;
            case McpServerResource r:
                var desc = r.IsTemplated ? r.ProtocolResourceTemplate.Description : r.ProtocolResource?.Description;
                if (string.IsNullOrWhiteSpace(desc)) issues.Add("No description.");
                break;
        }
        return issues;
    }

    private static List<string> UndescribedProperties(JsonElement schema)
    {
        var names = new List<string>();
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
            return names;
        foreach (var p in props.EnumerateObject())
            if (!(p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("description", out var d)
                  && d.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(d.GetString())))
                names.Add(p.Name);
        return names;
    }

    public static string Name(IMcpServerPrimitive p) => p switch
    {
        McpServerTool t => "tool " + t.ProtocolTool.Name,
        McpServerPrompt pr => "prompt " + pr.ProtocolPrompt.Name,
        McpServerResource { IsTemplated: true } r => "resource template " + r.ProtocolResourceTemplate.Name,
        McpServerResource r => "resource " + r.ProtocolResource?.Name,
        _ => p.ToString() ?? "primitive",
    };

    /// <summary>One line per primitive with issues, for the startup log.</summary>
    public static IEnumerable<string> Warnings(McpServerOptions server)
    {
        IEnumerable<IMcpServerPrimitive> all = [
            .. server.ToolCollection ?? [], .. server.PromptCollection ?? [], .. server.ResourceCollection ?? []];
        foreach (var p in all)
        {
            var issues = Check(p);
            if (issues.Count > 0) yield return $"MCP docs: {Name(p)}: {string.Join(" ", issues)}";
        }
    }
}
