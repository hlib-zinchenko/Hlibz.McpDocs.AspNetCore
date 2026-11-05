using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>
/// Builds <c>catalog.json</c> from what the SDK already registered (<see cref="McpServerOptions"/> collections),
/// not from our own reflection. Each tool/prompt/resource is serialized with the SDK's own JSON options,
/// so the definitions are byte-for-byte what <c>tools/list</c> etc. return.
/// <para>
/// The catalog is per viewer: primitives gated with <c>[Authorize]</c> are left out unless the viewer passes their
/// policy, exactly as <c>AddAuthorizationFilters()</c> filters the list methods. The serialized result is cached
/// per set of visible primitives, so the ETag still identifies content, not requests.
/// </para>
/// </summary>
internal sealed class McpDocsCatalog
{
    public sealed record Result(byte[] Json, string ETag);

    private enum Kind { Tool, Prompt, Resource, Template }

    private sealed record Item(Kind Kind, IMcpServerPrimitive Primitive, bool Gated);

    // Distinct visible sets seen in practice are one per role/policy combination; the cap only guards against a
    // pathological policy that varies per user.
    private const int MaxCachedVariants = 64;

    private readonly McpServerOptions _server;
    private readonly McpDocsOptions _docs;
    private readonly string _endpoint;
    private readonly string _pathBase;
    private readonly Item[] _items;
    private readonly int[] _gated; // indexes into _items
    private readonly ConcurrentDictionary<string, Result> _results = new(StringComparer.Ordinal);

    public McpDocsCatalog(McpServerOptions server, McpDocsOptions docs, string resolvedEndpoint, string pathBase = "")
    {
        _pathBase = pathBase;
        _server = server;
        _docs = docs;
        _endpoint = resolvedEndpoint;

        var tools = server.ToolCollection?.OrderBy(t => t.ProtocolTool.Name, StringComparer.Ordinal)
            .Select(t => (Kind.Tool, (IMcpServerPrimitive)t)) ?? [];
        var prompts = server.PromptCollection?.OrderBy(p => p.ProtocolPrompt.Name, StringComparer.Ordinal)
            .Select(p => (Kind.Prompt, (IMcpServerPrimitive)p)) ?? [];
        var allResources = server.ResourceCollection?.ToList() ?? [];
        var resources = allResources.Where(r => !r.IsTemplated && r.ProtocolResource is not null)
            .OrderBy(r => r.ProtocolResource!.Name, StringComparer.Ordinal).Select(r => (Kind.Resource, (IMcpServerPrimitive)r));
        var templates = allResources.Where(r => r.IsTemplated)
            .OrderBy(r => r.ProtocolResourceTemplate.Name, StringComparer.Ordinal).Select(r => (Kind.Template, (IMcpServerPrimitive)r));

        _items = tools.Concat(prompts).Concat(resources).Concat(templates)
            .Select(x => new Item(x.Item1, x.Item2, McpDocsAuthorization.IsGated(x.Item2))).ToArray();
        _gated = Enumerable.Range(0, _items.Length).Where(i => _items[i].Gated).ToArray();
    }

    public async ValueTask<Result> GetAsync(HttpContext ctx)
    {
        // Key: one char per gated primitive, '1' = visible. Empty when nothing is gated.
        var key = new char[_gated.Length];
        for (var i = 0; i < _gated.Length; i++)
            key[i] = await McpDocsAuthorization.IsAllowedAsync(ctx, _items[_gated[i]].Primitive) ? '1' : '0';
        var k = new string(key);

        if (_results.TryGetValue(k, out var cached)) return cached;

        var visible = new bool[_items.Length];
        for (var i = 0; i < _items.Length; i++) visible[i] = !_items[i].Gated;
        for (var i = 0; i < _gated.Length; i++) visible[_gated[i]] = key[i] == '1';

        var result = Build(visible);
        if (_results.Count < MaxCachedVariants) _results.TryAdd(k, result);
        return result;
    }

    private Result Build(bool[] visible)
    {
        var json = McpJsonUtilities.DefaultOptions;
        var toolInfo = (JsonTypeInfo<Tool>)json.GetTypeInfo(typeof(Tool));
        var promptInfo = (JsonTypeInfo<Prompt>)json.GetTypeInfo(typeof(Prompt));
        var resourceInfo = (JsonTypeInfo<Resource>)json.GetTypeInfo(typeof(Resource));
        var templateInfo = (JsonTypeInfo<ResourceTemplate>)json.GetTypeInfo(typeof(ResourceTemplate));
        var implInfo = (JsonTypeInfo<Implementation>)json.GetTypeInfo(typeof(Implementation));

        var info = _server.ServerInfo;
        var serverName = info?.Name ?? "mcp-server";

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();

            w.WritePropertyName("server");
            if (info is null) w.WriteNullValue(); else JsonSerializer.Serialize(w, info, implInfo);
            WriteString(w, "instructions", _server.ServerInstructions);
            WriteString(w, "protocolVersion", _server.ProtocolVersion);

            WriteCapabilities(w);

            w.WriteStartObject("docs");
            w.WriteBoolean("showMeta", _docs.ShowMeta);
            WriteString(w, "title", _docs.Title ?? info?.Title ?? serverName);
            WriteString(w, "description", _docs.Description);
            WriteString(w, "mcpEndpoint", _endpoint);
            WriteString(w, "serverKey", _docs.ServerKey ?? Slug(serverName));
            WriteString(w, "authHeader", _docs.AuthHeader);
            WriteString(w, "authPlaceholder", _docs.AuthHeader is null ? null : _docs.AuthPlaceholder);
            WriteString(w, "authHint", _docs.AuthHint);
            WriteString(w, "favicon", _docs.Favicon is null ? null : McpDocsTheme.Resolve(_docs.Favicon, _pathBase));
            w.WriteStartArray("links");
            foreach (var l in _docs.ToolbarItems)
            {
                w.WriteStartObject();
                w.WriteString("label", l.Label);
                w.WriteString("url", McpDocsTheme.Resolve(l.Url, _pathBase));
                w.WriteBoolean("newTab", l.NewTab);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            // Every entry: { group? (tools only), definition, authorization? }
            void Section(string name, Kind kind)
            {
                w.WriteStartArray(name);
                for (var i = 0; i < _items.Length; i++)
                {
                    var item = _items[i];
                    if (item.Kind != kind || !visible[i]) continue;

                    w.WriteStartObject();
                    w.WritePropertyName("definition");
                    switch (item.Primitive)
                    {
                        case McpServerTool t:
                            WriteDefinition(w, t.ProtocolTool, toolInfo);
                            break;
                        case McpServerPrompt p:
                            WriteDefinition(w, p.ProtocolPrompt, promptInfo);
                            break;
                        case McpServerResource r when kind == Kind.Resource:
                            WriteDefinition(w, r.ProtocolResource!, resourceInfo);
                            break;
                        case McpServerResource r:
                            WriteDefinition(w, r.ProtocolResourceTemplate, templateInfo);
                            break;
                    }
                    if (item.Primitive is McpServerTool tool) WriteString(w, "group", _docs.ToolGroup(tool));
                    if (item.Gated) McpDocsAuthorization.WriteRequirements(w, item.Primitive);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            Section("tools", Kind.Tool);
            Section("prompts", Kind.Prompt);
            Section("resources", Kind.Resource);
            Section("resourceTemplates", Kind.Template);

            w.WriteEndObject();
        }

        var bytes = ms.ToArray();
        return new Result(bytes, ETagOf(bytes));
    }

    // Streamed straight through unless _meta has to go: then via a node, so the rest stays what the SDK serialized.
    private void WriteDefinition<T>(Utf8JsonWriter w, T value, JsonTypeInfo<T> info)
    {
        if (_docs.ShowMeta) { JsonSerializer.Serialize(w, value, info); return; }
        var node = JsonSerializer.SerializeToNode(value, info);
        node?.AsObject().Remove("_meta");
        if (node is null) w.WriteNullValue(); else node.WriteTo(w);
    }

    /// <summary>
    /// Capabilities that are visible from the options: explicit <see cref="McpServerOptions.Capabilities"/> and
    /// handlers, which the SDK advertises as configured. Not shown, on purpose: <c>logging</c> (the SDK reports it
    /// without configuration) and <c>listChanged</c> (the SDK decides it per session, and dropped it when a
    /// value was set), since a chip here could contradict what <c>initialize</c> returns.
    /// </summary>
    private void WriteCapabilities(Utf8JsonWriter w)
    {
        var c = _server.Capabilities;
        var h = _server.Handlers;
        w.WriteStartObject("capabilities");
        w.WriteBoolean("completions", c?.Completions is not null || h?.CompleteHandler is not null);
        w.WriteBoolean("subscribe", c?.Resources?.Subscribe == true || h?.SubscribeToResourcesHandler is not null);
        w.WriteEndObject();
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
