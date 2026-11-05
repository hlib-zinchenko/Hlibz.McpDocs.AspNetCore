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
        };
        o.ServerInstructions = "Acme order management. Use get_order before cancelling anything.";
    })
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithTools([McpServerTool.Create(LambdaStyleTools.Ping, new() { Name = "ping", Title = "Ping (runtime-registered)", ReadOnly = true })])
    .WithPromptsFromAssembly()
    .WithResourcesFromAssembly()
    .AddAuthorizationFilters(); // hides [Authorize]d primitives from tools/list etc. — the docs page applies the same rules

var app = builder.Build();

app.MapMcp("/mcp");

app.MapMcpDocs("/mcp-docs", o =>
{
    o.Description = "Look up, create and cancel **Acme** orders from any MCP client. Read-only tools are safe to enable for everyone; " +
                    "destructive ones need the `orders:write` scope.";
    o.McpEndpoint = "/mcp";
    o.AuthHeader = "Authorization";
    o.AuthHint = "Ask in [#acme-integrations](https://example.com/slack) for a personal access token.";
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
