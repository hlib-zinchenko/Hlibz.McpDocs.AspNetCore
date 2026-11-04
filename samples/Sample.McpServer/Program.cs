using Hlibz.McpDocs.AspNetCore;
using ModelContextProtocol.Server;
using Sample.McpServer.Tools;

var builder = WebApplication.CreateBuilder(args);

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
    .WithResourcesFromAssembly();

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

app.Run();
