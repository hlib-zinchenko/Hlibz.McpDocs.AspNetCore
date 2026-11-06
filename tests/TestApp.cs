using System.ComponentModel;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Hlibz.McpDocs.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace Hlibz.McpDocs.AspNetCore.Tests;

/// <summary>A real server on TestServer: the MCP endpoint and the docs page side by side.</summary>
internal sealed class TestApp : IAsyncDisposable
{
    public required WebApplication App { get; init; }

    public static async Task<TestApp> StartAsync(Action<McpDocsOptions>? configure = null, bool requireAuth = false, Action<IApplicationBuilder>? beforeDocs = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddAuthorization();
        builder.Services.AddMcpServer(o =>
            {
                o.ServerInfo = new() { Name = "test-server", Title = "Test Server", Version = "1.0.0" };
                o.ServerInstructions = "Use the tools.";
            })
            .WithHttpTransport()
            .WithTools<Tools>()
            .WithTools([McpServerTool.Create(() => "pong", new() { Name = "ping", ReadOnly = true, Meta = new System.Text.Json.Nodes.JsonObject { ["owner"] = "team" } })])
            .WithPrompts<Prompts>()
            .WithResources<Resources>()
            .AddAuthorizationFilters();

        var app = builder.Build();
        if (beforeDocs is not null) beforeDocs(app);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMcp("/mcp");
        var group = app.MapMcpDocs("/mcp-docs", configure);
        if (requireAuth) group.RequireAuthorization();
        await app.StartAsync();
        return new TestApp { App = app };
    }

    public HttpClient Client(string? roles = null)
    {
        var c = App.GetTestClient();
        if (roles is not null) c.DefaultRequestHeaders.Add("X-Roles", roles);
        return c;
    }

    public async Task<McpClient> McpAsync(string? roles = null)
    {
        var http = Client(roles);
        var transport = new HttpClientTransport(new() { Endpoint = new Uri("http://localhost/mcp") }, http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    public ValueTask DisposeAsync() => App.DisposeAsync();

    public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Roles", out var roles)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim(ClaimTypes.Name, "tester"));
            var id = new ClaimsIdentity(claims, "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), "Test")));
        }
    }

    [McpServerToolType]
    public sealed class Tools
    {
        [McpServerTool(Name = "get_thing", ReadOnly = true), Description("Gets a thing.")]
        public string GetThing([Description("The id")] string id) => id;

        [McpServerTool(Name = "delete_thing", Destructive = true), Description("Deletes a thing.")]
        [Authorize(Roles = "admin")]
        public string DeleteThing([Description("The id")] string id) => id;

        [McpServerTool(Name = "list_things", ReadOnly = true, UseStructuredContent = true), Description("Lists things.")]
        public IReadOnlyList<Thing> ListThings() => [new("t-1", 2)];

        [McpServerTool(Name = "sloppy")]
        public string Sloppy(string id) => id;
    }

    public sealed record Thing([property: Description("Thing id")] string Id, int Count);

    [McpServerPromptType]
    public sealed class Prompts
    {
        [McpServerPrompt(Name = "greet"), Description("Greets someone.")]
        public string Greet([Description("Who")] string name) => "Hello " + name;
    }

    [McpServerResourceType]
    public sealed class Resources
    {
        [McpServerResource(UriTemplate = "thing://{id}", Name = "thing"), Description("A thing by id.")]
        public string Thing(string id) => id;

        [McpServerResource(UriTemplate = "docs://readme", Name = "readme"), Description("The readme.")]
        public string Readme() => "readme";
    }
}
