using System.Security.Claims;
using System.Text.Encodings.Web;

using Hlibz.McpDocs.AspNetCore;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

using Sample.McpServer.Tools;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Demo auth: a cookie set by /demo/sign-in, and a bearer token for MCP clients and the docs page's try-it console.
// The demo token is simply the roles it grants ("admin,finance"): never accept tokens like this outside a sample.
// A real server would use JWT bearer / OAuth. With GatedPrimitives = Hide the docs page evaluates [Authorize]
// against whoever the browser is signed in as (the cookie); try-it calls go out with the token.
builder.Services.AddAuthentication("Demo")
    .AddPolicyScheme("Demo", null, o => o.ForwardDefaultSelector = ctx =>
        ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? DemoBearerHandler.SchemeName : CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie()
    .AddScheme<AuthenticationSchemeOptions, DemoBearerHandler>(DemoBearerHandler.SchemeName, null);
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

// What get_order and cancel_order can fail with, in their _meta: the page lists them under Errors, with what to
// do about each. MCP has no error schema; a failed call is a result with isError and text content.
builder.Services.PostConfigure<McpServerOptions>(o =>
{
    McpDocsToolError notFound = new("order_not_found")
    {
        Description = "No order has the id.",
        Guidance = "Call `search_orders` to find the order's id, then try again.",
    };
    McpDocsToolErrors.Describe(o.ToolCollection!.First(t => t.ProtocolTool.Name == "get_order"), [notFound]);
    McpDocsToolErrors.Describe(o.ToolCollection!.First(t => t.ProtocolTool.Name == "cancel_order"),
    [
        notFound,
        new McpDocsToolError("order_shipped")
        {
            Description = "The order has shipped, so it can't be cancelled.",
            Guidance = "Don't retry. Offer the user a refund instead (`refund_order`, admins only).",
        },
    ]);
});

WebApplication app = builder.Build();

app.MapMcp("/mcp");

static void Docs(McpDocsOptions o)
{
    o.Description = "Look up, create and cancel **Acme** orders from any MCP client. Read-only tools are safe to enable for everyone; " +
                    "destructive ones need the `orders:write` scope.";
    o.AuthHeader = "Authorization";
    o.AuthHint = "Ask in [#acme-integrations](https://example.com/slack) for a personal access token. " +
                 "In this sample any token works: it is the roles it grants, e.g. `admin,finance`.";
    // A "Try it" button on every tool, prompt and resource; the token is kept for the tab.
    o.EnableTryIt(t => t.CredentialStorage = McpDocsTryItCredentialStorage.Session);
    o.AddToolbarItem("Changelog", "https://example.com/changelog");
    // [Authorize]d primitives are listed for everyone, with what they require. To list them per viewer instead,
    // uncomment this and sign in with /demo/sign-in?roles=admin,finance:
    // o.GatedPrimitives = McpDocsGatedPrimitives.Hide;
    // o.DefaultColorMode = McpDocsColorMode.Dark; o.DarkPreset = McpDocsDarkPreset.Nord; o.PrimaryColor = "#0a7f5a";
}

// Static export instead of serving: dotnet run --project samples/Sample.McpServer -- --export-html site/index.html
// The file is the same page with everything inline, for GitHub Pages or anywhere the app can't be reached.
if (args is ["--export-html", var file])
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
    await File.WriteAllTextAsync(file, await McpDocsSnapshot.CreateHtmlAsync(app.Services, Docs, mcpEndpoint: "https://example.com/mcp"));
    Console.WriteLine($"Wrote {Path.GetFullPath(file)}");
    return;
}

app.MapMcpDocs("/mcp-docs", Docs);
// .RequireAuthorization();   // the returned route group covers page, catalog.json and assets

app.MapGet("/", () => Results.Redirect("/mcp-docs/"));

if (app.Environment.IsDevelopment())
{
    // /demo/sign-in?roles=admin,finance → with GatedPrimitives = Hide, reload the docs page to see the gated primitives.
    app.MapGet("/demo/sign-in", async (HttpContext ctx, string? roles) =>
    {
        List<Claim> claims = new List<Claim> { new(ClaimTypes.Name, "demo") };
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

/// <summary>Sample only: accepts any bearer token and reads it as a comma-separated list of roles.</summary>
internal sealed class DemoBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DemoBearer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string[] roles = header["Bearer ".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (roles.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty token."));
        }

        ClaimsIdentity identity = new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim(ClaimTypes.Name, "token-user")), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
