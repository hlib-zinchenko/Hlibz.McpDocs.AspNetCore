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
            if (output["properties"] is JsonObject { Count: > 0 }) Parameters(sb, output);
            else
            {
                // Not an object (allowed since protocol 2026-07-28): a list or a bare value.
                sb.Append("Type: `").Append(TypeOf(output, output)).Append("`. Clients on protocol 2025-11-25 or older receive it wrapped as `{ \"result\": ... }`.\n");
                var (item, pointer) = Follow(output["items"], "#/items", output);
                if (item?["properties"] is JsonObject { Count: > 0 })
                {
                    sb.Append("\nEach item:\n\n");
                    Parameters(sb, item, output, pointer);
                }
            }
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

    private const int MaxNesting = 6;

    /// <summary>
    /// A parameter table. Nested fields get their own rows under a path (<c>address.city</c>, <c>lines[].sku</c>,
    /// <c>prices.&lt;key&gt;.amount</c>, <c>discount (option 2).amount</c>), following <c>$ref</c>; a type that
    /// contains itself is marked recursive and not expanded again.
    /// </summary>
    private static void Parameters(StringBuilder sb, JsonNode? schema, JsonNode? root = null, string pointer = "#")
    {
        if (schema?["properties"] is not JsonObject { Count: > 0 })
        {
            sb.Append(schema is null ? "None.\n" : "None (" + TypeOf(schema, root ?? schema) + ").\n");
            return;
        }
        sb.Append("| Name | Type | Required | Description |\n|---|---|---|---|\n");
        Rows(sb, schema.AsObject(), root ?? schema, pointer, "", [pointer], 0);
    }

    private static void Rows(StringBuilder sb, JsonObject obj, JsonNode root, string pointer, string prefix, HashSet<string> path, int depth)
    {
        if (obj["properties"] is not JsonObject props) return;
        var required = (obj["required"] as JsonArray)?.Select(x => (string?)x).ToHashSet() ?? [];
        foreach (var (name, raw) in props)
        {
            var p = pointer + "/properties/" + name.Replace("~", "~0").Replace("/", "~1");
            var (s, sp) = Follow(raw, p, root);
            var label = prefix + name;
            var recursive = Target(s, sp, root) is { } target && path.Contains(target);
            sb.Append("| `").Append(label).Append("` | ").Append(Cell(TypeOf(raw, root) + (recursive ? " (recursive)" : ""))).Append(" | ")
              .Append(required.Contains(name) ? "yes" : "no").Append(" | ")
              .Append(Cell((string?)raw?["description"] ?? (string?)s?["description"])).Append(" |\n");
            if (!recursive && depth < MaxNesting) Expand(sb, s, sp, root, label, path, depth + 1);
        }
    }

    /// <summary>Rows for what sits inside a value: an object's fields, an array's items, a map's values, union options.</summary>
    private static void Expand(StringBuilder sb, JsonNode? s, string sp, JsonNode root, string label, HashSet<string> path, int depth)
    {
        (s, sp) = Single(s, sp, root);
        if (s is not JsonObject o || path.Contains(sp)) return;
        path.Add(sp);
        if (o["properties"] is JsonObject { Count: > 0 })
            Rows(sb, o, root, sp, label + ".", path, depth);
        else if (o["items"] is JsonObject)
        {
            var (it, ip) = Follow(o["items"], sp + "/items", root);
            Expand(sb, it, ip, root, label + "[]", path, depth);
        }
        else if (o["additionalProperties"] is JsonObject)
        {
            var (v, vp) = Follow(o["additionalProperties"], sp + "/additionalProperties", root);
            Expand(sb, v, vp, root, label + ".<key>", path, depth);
        }
        else if (Variants(o, sp) is { } variants)
            foreach (var (v, vp, i) in variants)
            {
                var (r, rp) = Follow(v, vp, root);
                Expand(sb, r, rp, root, label + " (option " + (i + 1) + ")", path, depth);
            }
        path.Remove(sp);
    }

    /// <summary>The object an array, map or nullable wraps, by pointer: what "recursive" is checked against.</summary>
    private static string? Target(JsonNode? s, string sp, JsonNode root)
    {
        for (var guard = 0; guard < 10; guard++)
        {
            (s, sp) = Single(s, sp, root);
            if (s is not JsonObject o) return null;
            if (o["properties"] is JsonObject || Variants(o, sp) is not null) return sp;
            if (o["items"] is JsonObject) (s, sp) = Follow(o["items"], sp + "/items", root);
            else if (o["additionalProperties"] is JsonObject) (s, sp) = Follow(o["additionalProperties"], sp + "/additionalProperties", root);
            else return null;
        }
        return null;
    }

    /// <summary><c>anyOf</c>/<c>oneOf</c> options other than <c>null</c>, when there are two or more.</summary>
    private static List<(JsonNode? Node, string Pointer, int Index)>? Variants(JsonObject o, string sp)
    {
        var options = NonNullOptions(o, sp);
        return options is { Count: > 1 } ? options.Select((x, i) => (x.Node, x.Pointer, i)).ToList() : null;
    }

    /// <summary>Unwraps <c>anyOf: [X, null]</c> to X (after following any <c>$ref</c>).</summary>
    private static (JsonNode? Node, string Pointer) Single(JsonNode? s, string sp, JsonNode root)
    {
        if (s is JsonObject o && NonNullOptions(o, sp) is { Count: 1 } options) return Follow(options[0].Node, options[0].Pointer, root);
        return (s, sp);
    }

    private static List<(JsonNode? Node, string Pointer)>? NonNullOptions(JsonObject o, string sp)
    {
        foreach (var key in new[] { "anyOf", "oneOf" })
            if (o[key] is JsonArray a)
                return a.Select((v, i) => (Node: v, Pointer: $"{sp}/{key}/{i}")).Where(x => !IsNullType(x.Node)).ToList();
        return null;
    }

    private static bool IsNullType(JsonNode? n) =>
        n?["type"] is JsonValue v && v.TryGetValue<string>(out var t) && t == "null";

    /// <summary>Follows local <c>$ref</c>s (any <c>#</c> pointer, as the SDK emits <c>#/properties/x</c>) to the schema and its pointer.</summary>
    private static (JsonNode? Node, string Pointer) Follow(JsonNode? node, string pointer, JsonNode root)
    {
        for (var guard = 0; guard < 20 && (string?)node?["$ref"] is { } r && r.StartsWith('#'); guard++)
        {
            node = Resolve(node!, root);
            pointer = r;
        }
        return (node, pointer);
    }

    /// <summary>Follows one local <c>$ref</c> (<c>#</c>, <c>#/$defs/X</c>, <c>#/properties/x</c>, …) against the root schema.</summary>
    private static JsonNode? Resolve(JsonNode node, JsonNode root)
    {
        if ((string?)node["$ref"] is not { } r || !r.StartsWith('#')) return node;
        JsonNode? cur = root;
        foreach (var part in r[1..].Split('/', StringSplitOptions.RemoveEmptyEntries)) cur = cur?[part.Replace("~1", "/").Replace("~0", "~")];
        return cur;
    }

    /// <summary>
    /// A short type label: a <c>$defs</c> reference by name, arrays "of", unions joined, otherwise the schema
    /// <c>type</c>. Other local references are resolved against <paramref name="root"/> when it's given.
    /// </summary>
    internal static string TypeOf(JsonNode? n, JsonNode? root = null, int depth = 0)
    {
        if (n is not JsonObject o) return "any";
        if ((string?)o["$ref"] is { } r)
        {
            var named = r.StartsWith("#/$defs/", StringComparison.Ordinal) || r.StartsWith("#/definitions/", StringComparison.Ordinal);
            if (named || root is null || depth > 4 || Resolve(o, root) is not { } target) return r == "#" ? "object" : r[(r.LastIndexOf('/') + 1)..];
            return TypeOf(target, root, depth + 1);
        }
        if (o["enum"] is JsonArray e) return "enum(" + string.Join(", ", e.Select(x => x?.ToJsonString())) + ")";
        foreach (var key in new[] { "anyOf", "oneOf" })
            if (o[key] is JsonArray u) return string.Join(" | ", u.Select(x => TypeOf(x, root, depth + 1)).Distinct());
        var t = o["type"] switch
        {
            JsonArray a => string.Join(" | ", a.Select(x => (string?)x)),
            { } x => (string?)x,
            _ => null,
        };
        if (t == "array") return "array of " + TypeOf(o["items"], root, depth + 1);
        if (t == "object" && o["additionalProperties"] is JsonObject ap) return "map of " + TypeOf(ap, root, depth + 1);
        return t ?? (o["properties"] is JsonObject ? "object" : "any");
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
