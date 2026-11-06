using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>
/// Renders the per-viewer catalog as Markdown (for agents and grep), an <c>llms.txt</c> index, and a
/// <c>server.json</c> for the MCP Registry. All three read the catalog JSON, so they can't list anything
/// <c>catalog.json</c> doesn't.
/// </summary>
internal static class McpDocsExports
{
    public sealed record Source(JsonNode Catalog, string ETag, string Origin, string PathBase, string DocsPath);

    private static string Title(JsonNode c) => (string?)c["docs"]?["title"] ?? (string?)c["server"]?["name"] ?? "MCP server";

    private static string Endpoint(Source s)
    {
        var e = (string?)s.Catalog["docs"]?["mcpEndpoint"] ?? "/mcp";
        return e.StartsWith('/') ? s.Origin + e : e;
    }

    private static string DocsUrl(Source s, string file) => s.Origin + s.PathBase + s.DocsPath.TrimEnd('/') + "/" + file;

    // ---- catalog.md ----

    public static string Markdown(Source s)
    {
        var c = s.Catalog;
        var sb = new StringBuilder();
        sb.Append("# ").Append(Title(c)).Append('\n');
        if (Summary(c) is { } summary) sb.Append("\n> ").Append(OneLine(summary)).Append('\n');

        sb.Append("\n- Endpoint: ").Append(Endpoint(s)).Append(" (Streamable HTTP)\n");
        if ((string?)c["docs"]?["authHeader"] is { } header) sb.Append("- Auth header: `").Append(header).Append("`\n");
        if ((string?)c["docs"]?["authHint"] is { } hint) sb.Append("- Credentials: ").Append(OneLine(hint)).Append('\n');
        if ((string?)c["server"]?["version"] is { } version) sb.Append("- Version: ").Append(version).Append('\n');
        if ((string?)c["protocolVersion"] is { } pv) sb.Append("- Protocol version: ").Append(pv).Append('\n');
        if ((string?)c["instructions"] is { } instructions) sb.Append("\n## Instructions\n\n").Append(instructions.Trim()).Append('\n');

        Section(sb, c, "tools", "Tools", Tool);
        Section(sb, c, "prompts", "Prompts", Prompt);
        Section(sb, c, "resources", "Resources", Resource);
        Section(sb, c, "resourceTemplates", "Resource templates", Template);
        return sb.ToString();
    }

    private static void Section(StringBuilder sb, JsonNode c, string key, string heading, Action<StringBuilder, JsonNode, JsonNode> write)
    {
        if (c[key] is not JsonArray { Count: > 0 } items) return;
        sb.Append("\n## ").Append(heading).Append('\n');
        foreach (var item in items)
            write(sb, item!["definition"]!, item);
    }

    private static void Tool(StringBuilder sb, JsonNode d, JsonNode entry)
    {
        sb.Append("\n### ").Append((string?)d["name"]).Append('\n');
        if ((string?)entry["group"] is { } group) sb.Append("\nGroup: ").Append(group).Append('\n');
        Description(sb, d);
        Flags(sb, d["annotations"]);
        Authorization(sb, entry);

        sb.Append("\nParameters:\n\n");
        Parameters(sb, d["inputSchema"]);
        if (d["outputSchema"] is JsonObject output)
        {
            sb.Append("\nResult (`structuredContent`):\n\n");
            Parameters(sb, output);
        }
    }

    private static void Prompt(StringBuilder sb, JsonNode d, JsonNode entry)
    {
        sb.Append("\n### ").Append((string?)d["name"]).Append('\n');
        Description(sb, d);
        Authorization(sb, entry);
        if (d["arguments"] is JsonArray { Count: > 0 } args)
        {
            sb.Append("\nArguments:\n\n| Name | Required | Description |\n|---|---|---|\n");
            foreach (var a in args)
                sb.Append("| `").Append((string?)a!["name"]).Append("` | ").Append((bool?)a["required"] == true ? "yes" : "no")
                  .Append(" | ").Append(Cell((string?)a["description"])).Append(" |\n");
        }
        else sb.Append("\nNo arguments.\n");
    }

    private static void Resource(StringBuilder sb, JsonNode d, JsonNode entry)
    {
        sb.Append("\n### ").Append((string?)d["name"]).Append('\n');
        sb.Append("\nURI: `").Append((string?)d["uri"]).Append("`\n");
        if ((string?)d["mimeType"] is { } mime) sb.Append("\nMIME type: `").Append(mime).Append("`\n");
        Description(sb, d);
        Authorization(sb, entry);
    }

    private static void Template(StringBuilder sb, JsonNode d, JsonNode entry)
    {
        sb.Append("\n### ").Append((string?)d["name"]).Append('\n');
        sb.Append("\nURI template: `").Append((string?)d["uriTemplate"]).Append("`\n");
        if ((string?)d["mimeType"] is { } mime) sb.Append("\nMIME type: `").Append(mime).Append("`\n");
        Description(sb, d);
        Authorization(sb, entry);
    }

    private static void Description(StringBuilder sb, JsonNode d)
    {
        if ((string?)d["description"] is { Length: > 0 } text) sb.Append('\n').Append(text.Trim()).Append('\n');
    }

    private static void Flags(StringBuilder sb, JsonNode? a)
    {
        if (a is not JsonObject o) return;
        var flags = new List<string>();
        if ((bool?)o["readOnlyHint"] == true) flags.Add("read-only");
        // Spec defaults: destructive and open-world are true unless stated, for tools that are not read-only.
        if ((bool?)o["readOnlyHint"] != true && (bool?)o["destructiveHint"] != false) flags.Add("destructive");
        if ((bool?)o["idempotentHint"] == true) flags.Add("idempotent");
        if ((bool?)o["openWorldHint"] != false) flags.Add("open-world");
        sb.Append("\nAnnotations: ").Append(string.Join(", ", flags)).Append('\n');
    }

    private static void Authorization(StringBuilder sb, JsonNode entry)
    {
        if (entry["authorization"] is not JsonObject a) return;
        var parts = new List<string>();
        if (a["policies"] is JsonArray p) parts.AddRange(p.Select(x => $"policy `{(string?)x}`"));
        if (a["roles"] is JsonArray r)
            parts.AddRange(r.Select(set => "role " + string.Join(" or ", ((JsonArray)set!).Select(x => $"`{(string?)x}`"))));
        if ((bool?)a["custom"] == true) parts.Add("custom requirements");
        if (parts.Count > 0) sb.Append("\nRequires: ").Append(string.Join("; ", parts)).Append('\n');
    }

    private static void Parameters(StringBuilder sb, JsonNode? schema)
    {
        if (schema?["properties"] is not JsonObject { Count: > 0 } props)
        {
            sb.Append(schema is null ? "None.\n" : "None (" + TypeOf(schema) + ").\n");
            return;
        }
        var required = (schema["required"] as JsonArray)?.Select(x => (string?)x).ToHashSet() ?? [];
        sb.Append("| Name | Type | Required | Description |\n|---|---|---|---|\n");
        foreach (var (name, prop) in props)
            sb.Append("| `").Append(name).Append("` | ").Append(Cell(TypeOf(prop))).Append(" | ")
              .Append(required.Contains(name) ? "yes" : "no").Append(" | ").Append(Cell((string?)prop?["description"])).Append(" |\n");
    }

    /// <summary>A short type label: <c>$ref</c> by name, arrays "of", unions joined, otherwise the schema <c>type</c>.</summary>
    internal static string TypeOf(JsonNode? n)
    {
        if (n is not JsonObject o) return "any";
        if ((string?)o["$ref"] is { } r) return r[(r.LastIndexOf('/') + 1)..];
        if (o["enum"] is JsonArray e) return "enum(" + string.Join(", ", e.Select(x => x?.ToJsonString())) + ")";
        foreach (var key in new[] { "anyOf", "oneOf" })
            if (o[key] is JsonArray u) return string.Join(" | ", u.Select(TypeOf).Distinct());
        var t = o["type"] switch
        {
            JsonArray a => string.Join(" | ", a.Select(x => (string?)x)),
            { } x => (string?)x,
            _ => null,
        };
        if (t == "array") return "array of " + TypeOf(o["items"]);
        if (t == "object" && o["additionalProperties"] is JsonObject ap) return "map of " + TypeOf(ap);
        return t ?? "any";
    }

    private static string Cell(string? text) =>
        (text ?? "").Replace("|", "\\|").Replace("\r", "").Replace("\n", " ").Trim();

    private static string OneLine(string text) => text.Replace("\r", "").Replace("\n", " ").Trim();

    private static string? Summary(JsonNode c) => (string?)c["docs"]?["description"];

    // ---- llms.txt ----

    public static string LlmsTxt(Source s)
    {
        var c = s.Catalog;
        var sb = new StringBuilder();
        sb.Append("# ").Append(Title(c)).Append('\n');
        if (Summary(c) is { } summary) sb.Append("\n> ").Append(OneLine(summary)).Append('\n');
        sb.Append("\nMCP server over Streamable HTTP at ").Append(Endpoint(s)).Append(".\n");
        sb.Append("\n## Docs\n\n- [Full reference](").Append(DocsUrl(s, "catalog.md")).Append("): every tool, prompt and resource with its parameters\n");
        sb.Append("- [Machine-readable catalog](").Append(DocsUrl(s, "catalog.json")).Append("): the same as JSON, as `tools/list` returns it\n");

        List(sb, c, "tools", "Tools");
        List(sb, c, "prompts", "Prompts");
        List(sb, c, "resources", "Resources");
        List(sb, c, "resourceTemplates", "Resource templates");
        return sb.ToString();
    }

    private static void List(StringBuilder sb, JsonNode c, string key, string heading)
    {
        if (c[key] is not JsonArray { Count: > 0 } items) return;
        sb.Append("\n## ").Append(heading).Append("\n\n");
        foreach (var item in items)
        {
            var d = item!["definition"]!;
            var first = ((string?)d["description"] ?? "").Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
            sb.Append("- `").Append((string?)d["name"]).Append('`');
            if (first is not null) sb.Append(": ").Append(first);
            sb.Append('\n');
        }
    }

    // ---- server.json ----

    public static string ServerJson(Source s, string registryName)
    {
        var c = s.Catalog;
        var remote = new JsonObject { ["type"] = "streamable-http", ["url"] = Endpoint(s) };
        if ((string?)c["docs"]?["authHeader"] is { } header)
        {
            var h = new JsonObject { ["name"] = header };
            if ((string?)c["docs"]?["authPlaceholder"] is { } placeholder) h["value"] = placeholder;
            h["isSecret"] = true;
            h["isRequired"] = true;
            if ((string?)c["docs"]?["authHint"] is { } hint) h["description"] = OneLine(hint);
            remote["headers"] = new JsonArray(h);
        }

        var doc = new JsonObject
        {
            ["$schema"] = "https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json",
            ["name"] = registryName,
            ["description"] = Summary(c) ?? Title(c),
            ["version"] = (string?)c["server"]?["version"] ?? "0.0.0",
            ["remotes"] = new JsonArray(remote),
        };
        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }
}
