using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>
/// Builds <c>catalog.json</c> from what the SDK already registered (<see cref="McpServerOptions"/> collections),
/// not from our own reflection. Each tool/prompt/resource is serialized with the SDK's own JSON options,
/// so the definitions are byte-for-byte what <c>tools/list</c> etc. return.
/// </summary>
internal static class McpDocsCatalog
{
    public sealed record Result(byte[] Json, string ETag, int Tools, int Prompts, int Resources, int Templates);

    public static Result Build(McpServerOptions server, McpDocsOptions docs, string resolvedEndpoint)
    {
        var json = McpJsonUtilities.DefaultOptions;
        var toolInfo = (JsonTypeInfo<Tool>)json.GetTypeInfo(typeof(Tool));
        var promptInfo = (JsonTypeInfo<Prompt>)json.GetTypeInfo(typeof(Prompt));
        var resourceInfo = (JsonTypeInfo<Resource>)json.GetTypeInfo(typeof(Resource));
        var templateInfo = (JsonTypeInfo<ResourceTemplate>)json.GetTypeInfo(typeof(ResourceTemplate));
        var implInfo = (JsonTypeInfo<Implementation>)json.GetTypeInfo(typeof(Implementation));

        var tools = server.ToolCollection?.OrderBy(t => t.ProtocolTool.Name, StringComparer.Ordinal).ToList() ?? [];
        var prompts = server.PromptCollection?.OrderBy(p => p.ProtocolPrompt.Name, StringComparer.Ordinal).ToList() ?? [];
        var allResources = server.ResourceCollection?.ToList() ?? [];
        var resources = allResources.Where(r => !r.IsTemplated && r.ProtocolResource is not null)
            .Select(r => r.ProtocolResource!).OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        var templates = allResources.Where(r => r.IsTemplated)
            .Select(r => r.ProtocolResourceTemplate).OrderBy(r => r.Name, StringComparer.Ordinal).ToList();

        var info = server.ServerInfo;
        var serverName = info?.Name ?? "mcp-server";

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();

            w.WritePropertyName("server");
            if (info is null) w.WriteNullValue(); else JsonSerializer.Serialize(w, info, implInfo);
            WriteString(w, "instructions", server.ServerInstructions);
            WriteString(w, "protocolVersion", server.ProtocolVersion);

            w.WriteStartObject("docs");
            WriteString(w, "title", docs.Title ?? info?.Title ?? serverName);
            WriteString(w, "description", docs.Description);
            WriteString(w, "mcpEndpoint", resolvedEndpoint);
            WriteString(w, "serverKey", docs.ServerKey ?? Slug(serverName));
            WriteString(w, "authHeader", docs.AuthHeader);
            WriteString(w, "authPlaceholder", docs.AuthHeader is null ? null : docs.AuthPlaceholder);
            WriteString(w, "authHint", docs.AuthHint);
            w.WriteEndObject();

            w.WriteStartArray("tools");
            foreach (var t in tools)
            {
                w.WriteStartObject();
                WriteString(w, "group", docs.ToolGroup(t));
                w.WritePropertyName("definition");
                JsonSerializer.Serialize(w, t.ProtocolTool, toolInfo);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("prompts");
            foreach (var p in prompts) JsonSerializer.Serialize(w, p.ProtocolPrompt, promptInfo);
            w.WriteEndArray();

            w.WriteStartArray("resources");
            foreach (var r in resources) JsonSerializer.Serialize(w, r, resourceInfo);
            w.WriteEndArray();

            w.WriteStartArray("resourceTemplates");
            foreach (var r in templates) JsonSerializer.Serialize(w, r, templateInfo);
            w.WriteEndArray();

            w.WriteEndObject();
        }

        var bytes = ms.ToArray();
        return new Result(bytes, ETagOf(bytes), tools.Count, prompts.Count, resources.Count, templates.Count);
    }

    public static string ETagOf(ReadOnlySpan<byte> bytes) =>
        "\"" + Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant() + "\"";

    private static void WriteString(Utf8JsonWriter w, string name, string? value)
    {
        if (value is not null) w.WriteString(name, value);
    }

    private static string Slug(string s)
    {
        var chars = s.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }
}
