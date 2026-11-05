using System.Security.Claims;
using Hlibz.McpDocs.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ModelContextProtocol.Server;
using Sample.McpServer.Tools;

var builder = WebApplication.CreateBuilder(args);

// Demo auth: a cookie set by /demo/sign-in. A real server would use JWT bearer / OAuth for MCP clients;
// the docs page evaluates [Authorize] against whoever the browser is signed in as.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Finance", p => p.RequireRole("finance"));

builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new()
        {
            Name = "acme-orders",
            Title = "Acme Orders",
            Version = "1.2.3",
            WebsiteUrl = "https://example.com/acme-orders",
            Icons = [new() { Source = "data:image/svg+xml;utf8,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'%3E%3Crect width='16' height='16' rx='3' fill='%234f46e5'/%3E%3Cpath d='M4 8h8M8 4v8' stroke='white' stroke-width='2'/%3E%3C/svg%3E", MimeType = "image/svg+xml" }],
        };
        o.ServerInstructions = "Acme order management. Use get_order before cancelling anything.";
    })
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithTools([McpServerTool.Create(LambdaStyleTools.Ping, new() { Name = "ping", Title = "Ping (runtime-registered)", ReadOnly = true, Meta = new System.Text.Json.Nodes.JsonObject { ["owner"] = "platform-team" } })])
    .WithPromptsFromAssembly()
    .WithResourcesFromAssembly()
    .AddAuthorizationFilters(); // hides [Authorize]d primitives from tools/list etc. — the docs page applies the same rules

var app = builder.Build();

app.MapMcp("/mcp");

app.MapMcpDocs("/mcp-docs", o =>
{
    o.Description = "Look up, create and cancel **Acme** orders from any MCP client. Read-only tools are safe to enable for everyone; " +
                    "destructive ones need the `orders:write` scope.";
    o.AuthHeader = "Authorization";
    o.AuthHint = "Ask in [#acme-integrations](https://example.com/slack) for a personal access token.";
    o.AddToolbarItem("Changelog", "https://example.com/changelog");
    // o.DefaultColorMode = McpDocsColorMode.Dark; o.DarkPreset = McpDocsDarkPreset.Nord; o.PrimaryColor = "#0a7f5a";
});
// .RequireAuthorization();   // the returned route group covers page, catalog.json and assets

app.MapGet("/", () => Results.Redirect("/mcp-docs/"));

if (app.Environment.IsDevelopment())
{
    // /demo/sign-in?roles=admin,finance → reload the docs page to see the gated primitives.
    app.MapGet("/demo/sign-in", async (HttpContext ctx, string? roles) =>
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "demo") };
        claims.AddRange((roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => new Claim(ClaimTypes.Role, r)));
        await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        return Results.Redirect("/mcp-docs/");
    });
    app.MapGet("/demo/sign-out", async (HttpContext ctx) =>
    {
        await ctx.SignOutAsync();
        return Results.Redirect("/mcp-docs/");
    });
}

app.Run();
