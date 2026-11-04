# Hlibz.McpDocs.AspNetCore

A read-only, ReDoc-style documentation page for MCP servers hosted in ASP.NET Core.
Shows what the server offers (tools, prompts, resources) and how to connect to it.

> Status: proof of concept. Not published to NuGet yet.

```bash
dotnet run --project samples/Sample.McpServer --launch-profile http
```

Then open http://localhost:5168/mcp-docs/ (raw data: http://localhost:5168/mcp-docs/catalog.json).

## Usage

```csharp
builder.Services.AddMcpServer(...).WithHttpTransport().WithToolsFromAssembly()...;

app.MapMcp("/mcp");
app.MapMcpDocs("/mcp-docs", o =>
{
    o.Description = "What this server is for, **Markdown** subset.";
    o.McpEndpoint = "/mcp";            // used in the connect snippets
    o.AuthHeader = "Authorization";    // optional: adds the header to the snippets
    o.AuthHint = "Where to get a token.";
    o.ToolGroup = McpDocsGrouping.ByAttribute;  // default; or ByNamePrefix, None, or your own func
})
.RequireAuthorization();               // RouteGroupBuilder: covers page, catalog.json and assets
```

Group tools with `[McpDocsGroup("Orders")]` on the tool class or method.

## Layout

```
src/Hlibz.McpDocs.AspNetCore/
  McpDocsOptions.cs                          options, [McpDocsGroup], grouping strategies
  McpDocsCatalog.cs                          catalog.json from McpServerOptions collections
  McpDocsEndpointRouteBuilderExtensions.cs   MapMcpDocs: page, catalog.json, _assets/*
  Assets/index.html, app.css, app.js         embedded UI (vanilla JS, no deps, no inline script)
samples/Sample.McpServer/                    10 tools (incl. runtime-registered `ping`, recursive output,
                                             dictionary input, polymorphic anyOf), 2 prompts, 1 resource, 1 template
```

## What the proof of concept answered

- **Data source works.** `IOptions<McpServerOptions>.Value.ToolCollection / PromptCollection / ResourceCollection`
  are populated for both attribute-based and runtime-registered (`McpServerTool.Create`) primitives. Serializing
  `ProtocolTool` etc. with `McpJsonUtilities.DefaultOptions` gives output **identical** to live `tools/list`,
  `prompts/list`, `resources/list` and `resources/templates/list` (checked by diffing all four).
- **No declaring type for grouping.** `AIFunctionMcpServerTool` (which has the `MethodInfo`) is internal, and
  `Metadata` holds attribute *instances* only. So grouping reads our own `[McpDocsGroup]` from `Metadata` (class attributes
  come before method attributes, so the method one wins). Runtime tools land in "Other".
- **Schema renderer is most of the UI work, and doable.** ~250 lines cover nested objects, arrays, maps
  (`additionalProperties`), `$ref` incl. the SDK's root-recursive `"$ref": "#"`, `anyOf`/`oneOf` variants,
  nullable via type arrays, enum/const/default/min/max/pattern chips, and example generation for the right panel.
- **Serving:** `catalog.json` cached per `PathBase`, ETag = SHA-256 of content → 304s; assets
  `immutable` with a content-hash `?v=`; page CSP `script-src 'self'; style-src 'self'` holds (nothing inline);
  `RequestDelegate` endpoints, so the library builds with `IsAotCompatible` and no trim warnings.

## Page

- Overview: title, version, description, model instructions, endpoint with copy, auth note.
- Connect panel: Claude Code / Claude Desktop / VS Code / Cursor / Other, generated from endpoint + auth header.
- Sidebar: grouped tools with risk dots (green read-only, amber modifies, red destructive), prompts, resources;
  search (`/` to focus); scroll spy; deep links `#tool/<name>`.
- Tool: annotation badges with spec defaults applied, parameters tree, result tree (when `outputSchema`),
  example `tools/call` request and response.
- Light/dark: system default, toggle remembered in `localStorage`.

## Not done / open

- Dynamic servers (custom `ListToolsHandler`, per-caller filtering) — page shows only what's registered.
- Tests (real Kestrel), multi-targeting net8/9, packaging.
- Markdown is a small safe subset, not CommonMark.
- Connect snippet formats are from memory — verify against current client docs before shipping.
- Icons (`Implementation.Icons`, tool icons) are ignored.
