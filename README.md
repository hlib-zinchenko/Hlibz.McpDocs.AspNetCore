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
    // o.McpEndpoint = "/mcp";         // optional: found from MapMcp; a path that matches no route stops startup
    o.AuthHeader = "Authorization";    // optional: adds the header to the snippets
    o.AuthHint = "Where to get a token.";
    o.ToolGroup = McpDocsGrouping.ByAttribute;  // default; or ByNamePrefix, None, or your own func
    o.ShowMeta = false;                         // true: show each primitive's _meta (also in catalog.json)
    o.DefaultColorMode = McpDocsColorMode.System;   // first-time viewers: System, Light or Dark
    o.PrimaryColor = "#0a7f5a";                     // validated: hex, rgb(), hsl() or a colour name
    o.DarkModePrimaryColor = "#4fc99a";
    o.DarkPreset = McpDocsDarkPreset.Nord;          // Default, Midnight, Nord, GitHubDark
    o.AdditionalCss = "h1 { letter-spacing: -.01em; }";  // served as _assets/theme.css, CSP stays strict
    o.Favicon = "~/favicon.ico";                    // https:, data:image/ or a path; default: the server icon
    o.AddToolbarItem("Changelog", "https://example.com/changelog");  // "~/x" resolves against PathBase
})
.RequireAuthorization();               // RouteGroupBuilder: covers page, catalog.json and assets
```

Group tools with `[McpDocsGroup("Orders")]` on the tool class or method.

## Authorization

`[Authorize]` on tools, prompts and resources is honoured the way the SDK's `AddAuthorizationFilters()` honours it
for `tools/list` etc.: a viewer only sees the primitives their MCP client would see. Same rules — any
`[AllowAnonymous]` on the class or method wins; otherwise `IAuthorizeData`, `AuthorizationPolicy` and
`IAuthorizationRequirementData` metadata are combined into one policy and evaluated with `IAuthorizationService`
against `HttpContext.User`. Gated primitives the viewer *can* see get a lock badge naming the roles / policies.

Two differences from the MCP endpoint, both inherent to a page opened in a browser:

- **Identity is the browser's.** The docs request is authenticated by whatever the app's default scheme sees on
  it (usually a cookie), while MCP clients typically send a bearer token. If the browser isn't signed in, the page
  shows the anonymous view.
- **The authorization resource is the `HttpContext`**, not the SDK's MCP request context. Resource-based
  handlers that look for the MCP context won't match here, and the primitive stays hidden.

`catalog.json` is served `private, no-cache`; the ETag still hashes the content, so viewers with the same
access share a cached build and get 304s.

The sample signs in with a demo cookie: `/demo/sign-in?roles=admin,finance`, `/demo/sign-out`.

## Layout

```
src/Hlibz.McpDocs.AspNetCore/
  McpDocsOptions.cs                          options, [McpDocsGroup], grouping strategies
  McpDocsCatalog.cs                          catalog.json from McpServerOptions collections, per viewer
  McpDocsAuthorization.cs                    [Authorize] evaluation, mirrors the SDK's list filters
  McpDocsEndpointRouteBuilderExtensions.cs   MapMcpDocs: page, catalog.json, _assets/*
  Assets/index.html, app.css, app.js         embedded UI (vanilla JS, no deps, no inline script)
samples/Sample.McpServer/                    13 tools (incl. runtime-registered `ping`, recursive output,
                                             dictionary input, polymorphic anyOf, [Authorize]d admin tools),
                                             3 prompts, 2 resources, 1 template; demo cookie sign-in
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
- **Serving:** `catalog.json` cached per `PathBase` and set of visible primitives, ETag = SHA-256 of content → 304s; assets
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

- Dynamic servers (custom `ListToolsHandler`, custom list filters) — page shows only what's registered,
  filtered by `[Authorize]` only.
- Tests (real Kestrel), multi-targeting net8/9, packaging.
- Markdown is a small safe subset, not CommonMark.
- Connect snippet formats are from memory — verify against current client docs before shipping.
- Icons (`Implementation.Icons`, tool icons) are ignored.
