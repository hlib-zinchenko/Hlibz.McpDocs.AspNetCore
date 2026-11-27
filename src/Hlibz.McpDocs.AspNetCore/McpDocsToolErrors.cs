using System.Text.Json.Nodes;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>
/// The errors a tool can return. MCP declares no error shape: a failed call is a result with <c>isError</c> and
/// (usually text) content, so nothing in a tool's definition says which failures to expect or what to do about
/// them. These go in the tool's <c>_meta</c> under <see cref="MetaKey"/>, where any client can read them, and the
/// docs page lists them on the tool (and <c>catalog.json</c> as the entry's <c>errors</c>), whether or not
/// <see cref="McpDocsOptions.ShowMeta"/> is set.
/// <para>
/// The <c>_meta</c> value is an array of <c>{ "code": string, "description"?: string, "guidance"?: string }</c>; a
/// server can write it itself, or call <see cref="Describe"/>. Entries without a code are skipped.
/// </para>
/// </summary>
public static class McpDocsToolErrors
{
    /// <summary>The <c>_meta</c> key a tool's errors are listed under.</summary>
    public const string MetaKey = "hlibz/errors";

    /// <summary>
    /// Lists <paramref name="errors"/> in <paramref name="tool"/>'s <c>_meta</c>, replacing what was listed. Call it
    /// before the server starts answering, e.g. in a <c>PostConfigure&lt;McpServerOptions&gt;</c> once the tools are
    /// registered.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <param name="errors">Its errors, in the order to show them.</param>
    public static void Describe(McpServerTool tool, IEnumerable<McpDocsToolError> errors)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(errors);
        JsonArray list = [];
        foreach (McpDocsToolError error in errors)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(error.Code, nameof(errors));
            JsonObject entry = new JsonObject { ["code"] = error.Code };
            if (error.Description is not null)
            {
                entry["description"] = error.Description;
            }

            if (error.Guidance is not null)
            {
                entry["guidance"] = error.Guidance;
            }

            list.Add((JsonNode)entry);
        }

        Tool protocol = tool.ProtocolTool;
        protocol.Meta ??= [];
        protocol.Meta[MetaKey] = list;
    }

    /// <summary>The well-formed entries <paramref name="tool"/>'s <c>_meta</c> lists; <see langword="null"/> for none.</summary>
    internal static JsonArray? Read(Tool tool)
    {
        if (tool.Meta?[MetaKey] is not JsonArray entries)
        {
            return null;
        }

        JsonArray errors = [];
        foreach (JsonObject entry in entries.OfType<JsonObject>())
        {
            if (Text(entry, "code") is not { } code)
            {
                continue;
            }

            JsonObject copy = new JsonObject { ["code"] = code };
            if (Text(entry, "description") is { } description)
            {
                copy["description"] = description;
            }

            if (Text(entry, "guidance") is { } guidance)
            {
                copy["guidance"] = guidance;
            }

            errors.Add((JsonNode)copy);
        }

        return errors.Count > 0 ? errors : null;
    }

    private static string? Text(JsonObject entry, string name) =>
        entry[name] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}

/// <summary>An error a tool can return, for <see cref="McpDocsToolErrors.Describe"/>.</summary>
/// <param name="Code">What identifies the error to a client: what it branches on.</param>
public sealed record McpDocsToolError(string Code)
{
    /// <summary>Gets what the error means. Markdown.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets what to do about it: what a model should try next, in terms of the server's tools. Markdown;
    /// <c>{placeholders}</c> for values the error result fills in are shown as code.
    /// </summary>
    public string? Guidance { get; init; }
}
