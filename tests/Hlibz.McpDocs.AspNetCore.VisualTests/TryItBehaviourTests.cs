using System.ComponentModel;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using Xunit;

namespace Hlibz.McpDocs.AspNetCore.VisualTests;

/// <summary>
/// The try-it console in a real browser against a real MCP endpoint: what it sends, and how each kind of answer
/// and failure shows.
/// </summary>
[Trait("Category", "Behaviour")]
public class TryItBehaviourTests : IAsyncLifetime
{
    private VisualApp _app = null!;
    private IPlaywright _pw = null!;
    private IBrowser _browser = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await StartAsync(t => t.CredentialStorage = McpDocsTryItCredentialStorage.Session);
        _pw = await Playwright.CreateAsync();
        _browser = await _pw.Chromium.LaunchAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _browser.DisposeAsync();
        _pw.Dispose();
        await _app.DisposeAsync();
    }

    private static Task<VisualApp> StartAsync(Action<McpDocsTryItOptions>? tryIt = null, Action<IEndpointConventionBuilder>? mcpEndpoint = null, bool enable = true) =>
        VisualApp.StartAsync(
            server: s =>
            {
                s.Services.AddAuthentication(TokenRoles.SchemeName).AddScheme<AuthenticationSchemeOptions, TokenRoles>(TokenRoles.SchemeName, null);
                s.Services.AddAuthorization();
                s.WithTools<TryTools>().WithResources<TryResources>().AddAuthorizationFilters();
            },
            docs: o =>
            {
                o.AuthHeader = "Authorization";
                if (enable)
                {
                    o.EnableTryIt(tryIt);
                }
            },
            mcpEndpoint: mcpEndpoint);

    /// <summary>Test only: a bearer token is the roles it grants.</summary>
    public sealed class TokenRoles(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        public const string SchemeName = "TokenRoles";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            IEnumerable<Claim> claims = header[7..].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 401;
            Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }
    }

    [McpServerToolType]
    public sealed class TryTools
    {
        [McpServerTool(Name = "echo", ReadOnly = true), Description("Echoes.")]
        public string Echo([Description("What to say")] string message) => message;

        [McpServerTool(Name = "fail", ReadOnly = true), Description("Always fails.")]
        public string Fail() => throw new McpException("Out of stock.");

        [McpServerTool(Name = "rename", Destructive = false), Description("Renames.")]
        public string Rename([Description("New name")] string name) => name;

        [McpServerTool(Name = "wipe", Destructive = true), Description("Wipes everything.")]
        [Authorize(Roles = "admin")]
        public string Wipe() => "wiped";

        [McpServerTool(Name = "regional", ReadOnly = true), Description("Per region.")]
        public string Regional([McpHeader("Region"), Description("Region")] string region) => "in " + region;
    }

    [McpServerResourceType]
    public sealed class TryResources
    {
        [McpServerResource(UriTemplate = "thing://{id}", Name = "thing"), Description("A thing by id.")]
        public string Thing(string id) => "thing " + id;
    }

    private async Task<IPage> OpenAsync(VisualApp? app = null)
    {
        IPage page = await (await _browser.NewContextAsync()).NewPageAsync();
        List<string> errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        await page.GotoAsync($"{(app ?? _app).BaseUrl}/mcp-docs/");
        await page.WaitForSelectorAsync("main .prop-name");
        Assert.Empty(errors);
        return page;
    }

    private static async Task TryAsync(IPage page, string id)
    {
        await page.ClickAsync($"[id=\"{id}\"] .btn.try");
        await page.WaitForSelectorAsync("dialog.ti[open]");
    }

    // Sends and waits for the answer; returns the request as the server received it.
    private static async Task<Dictionary<string, string>> SendAsync(IPage page)
    {
        IRequest request = await page.RunAndWaitForRequestAsync(() => page.ClickAsync("dialog.ti .ti-send"), r => r.Url.EndsWith("/mcp", StringComparison.Ordinal));
        await page.WaitForFunctionAsync("() => { const r = document.querySelector('dialog.ti .ti-response'); return r && r.children.length && !r.querySelector('.ti-pending'); }");
        return await request.AllHeadersAsync();
    }

    private static Task<T> Eval<T>(IPage page, string js) => page.EvaluateAsync<T>(js);
    private static Task<string[]> Texts(IPage page, string selector) => Eval<string[]>(page, $"[...document.querySelectorAll('dialog.ti {selector}')].map(e => e.textContent)");

    [Fact]
    public async Task Without_EnableTryIt_there_is_no_button()
    {
        await using VisualApp off = await StartAsync(enable: false);
        IPage page = await OpenAsync(off);
        Assert.Equal(0, await Eval<int>(page, "document.querySelectorAll('.btn.try').length"));
        Assert.Contains("read-only docs", await Eval<string>(page, "document.querySelector('.side-foot').textContent"));
    }

    [Fact]
    public async Task A_tool_call_sends_the_mcp_headers_and_shows_what_came_back()
    {
        IPage page = await OpenAsync();
        await TryAsync(page, "tool/echo");
        Assert.Contains("\"message\": \"string\"", await Eval<string>(page, "document.querySelector('dialog.ti .ti-json').value")); // prefilled from the schema
        await page.FillAsync("dialog.ti .ti-json", "{ \"message\": \"hi there\" }");
        Dictionary<string, string> sent = await SendAsync(page);

        Assert.Equal("tools/call", sent["mcp-method"]);
        Assert.Equal("echo", sent["mcp-name"]);
        Assert.Equal("2026-07-28", sent["mcp-protocol-version"]);
        Assert.Equal(["hi there"], await Texts(page, ".ti-response .ti-text"));
        Assert.Equal("badge ok", await Eval<string>(page, "document.querySelector('dialog.ti .ti-status .badge').className"));

        // Closing keeps what was typed and the response for this tool.
        await page.Keyboard.PressAsync("Escape");
        Assert.False(await Eval<bool>(page, "document.querySelector('dialog.ti').open"));
        Assert.Equal("Try echo", await Eval<string>(page, "document.activeElement.getAttribute('aria-label')")); // focus back on the opener
        await TryAsync(page, "tool/echo");
        Assert.Contains("hi there", await Eval<string>(page, "document.querySelector('dialog.ti .ti-json').value"));
        Assert.Equal(["hi there"], await Texts(page, ".ti-response .ti-text"));
    }

    [Fact]
    public async Task The_token_goes_as_a_bearer_token_and_decides_what_a_gated_tool_allows()
    {
        IPage page = await OpenAsync();
        await TryAsync(page, "tool/wipe");
        Dictionary<string, string> anonymous = await SendAsync(page);
        Assert.False(anonymous.ContainsKey("authorization"));
        Assert.Contains(await Texts(page, ".ti-banner"), b => b.StartsWith("JSON-RPC error", StringComparison.Ordinal));
        Assert.Equal("badge warn", await Eval<string>(page, "document.querySelector('dialog.ti .ti-status .badge').className"));

        await page.FillAsync("#ti-token", "admin");
        Dictionary<string, string> admin = await SendAsync(page);
        Assert.Equal("Bearer admin", admin["authorization"]);
        Assert.Equal(["wiped"], await Texts(page, ".ti-response .ti-text"));

        // On screen the request shows the scheme, not the token.
        string preview = await Eval<string>(page, "document.querySelector('dialog.ti .ti-request').textContent");
        Assert.Contains("Authorization: Bearer ••••••", preview);
        Assert.DoesNotContain("admin", preview);
    }

    [Fact]
    public async Task A_tool_error_shows_the_isError_banner_and_its_text()
    {
        IPage page = await OpenAsync();
        await TryAsync(page, "tool/fail");
        await SendAsync(page);
        Assert.Contains("The tool reported an error (isError: true).", await Texts(page, ".ti-banner"));
        Assert.Contains(await Texts(page, ".ti-response .ti-text"), t => t.Contains("Out of stock.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_prompt_shows_the_messages_it_expands_to()
    {
        IPage page = await OpenAsync();
        await TryAsync(page, "prompt/greet");
        await page.ClickAsync("dialog.ti .ti-send");
        Assert.Equal(["Fill in name."], await Texts(page, ".ti-error:not([hidden])")); // required arguments are checked first
        await page.FillAsync("dialog.ti input[data-name=name]", "Ann");
        Dictionary<string, string> sent = await SendAsync(page);
        Assert.Equal("prompts/get", sent["mcp-method"]);
        Assert.Equal(["user"], await Texts(page, ".ti-role"));
        Assert.Equal(["Hello Ann"], await Texts(page, ".ti-msg .ti-text"));
    }

    [Fact]
    public async Task A_resource_template_expands_its_variables_into_the_uri()
    {
        IPage page = await OpenAsync();
        await TryAsync(page, "template/thing");
        await page.FillAsync("dialog.ti input[data-name=id]", "a b/c");
        Assert.Equal(["thing://a%20b%2Fc"], await Texts(page, ".ti-uri code"));
        Dictionary<string, string> sent = await SendAsync(page);
        Assert.Equal("thing://a%20b%2Fc", sent["mcp-name"]);
        Assert.Equal(["thing a b/c"], await Texts(page, ".ti-response .ti-contents .ti-text"));
    }

    [Fact]
    public async Task McpHeader_parameters_are_mirrored_into_headers()
    {
        IPage page = await OpenAsync();
        await TryAsync(page, "tool/regional");
        await page.FillAsync("dialog.ti .ti-json", "{ \"region\": \"Zürich\" }");
        Dictionary<string, string> sent = await SendAsync(page);
        Assert.Equal("=?base64?WsO8cmljaA==?=", sent["mcp-param-region"]);
        Assert.Equal(["in Zürich"], await Texts(page, ".ti-response .ti-text"));
    }

    [Fact]
    public async Task Send_is_coloured_by_what_a_tool_may_do()
    {
        IPage page = await OpenAsync();
        foreach ((string id, string level) in new[] { ("tool/echo", "ok"), ("tool/rename", "warn"), ("tool/wipe", "danger"), ("prompt/greet", "ok"), ("template/thing", "ok") })
        {
            await TryAsync(page, id);
            Assert.Equal("ti-send " + level, await Eval<string>(page, "document.querySelector('dialog.ti .ti-send').className"));
            Assert.Equal(level == "ok" ? 0 : 1, await Eval<int>(page, "document.querySelectorAll('dialog.ti .ti-note').length"));
            await page.Keyboard.PressAsync("Escape");
        }
    }

    [Fact]
    public async Task A_401_from_the_endpoint_says_the_token_is_missing_or_invalid()
    {
        await using VisualApp gated = await StartAsync(mcpEndpoint: e => e.RequireAuthorization());
        IPage page = await OpenAsync(gated);
        await TryAsync(page, "tool/echo");
        await SendAsync(page);
        Assert.Equal(["Unauthorized: the token is missing or invalid.WWW-Authenticate: Bearer"], await Texts(page, ".ti-banner"));
        Assert.Equal("badge danger", await Eval<string>(page, "document.querySelector('dialog.ti .ti-status .badge').className"));

        await page.FillAsync("#ti-token", "reader");
        await SendAsync(page);
        Assert.Equal(["string"], await Texts(page, ".ti-response .ti-text"));
    }

    [Fact]
    public async Task ReadOnlyOnly_leaves_out_tools_that_may_change_data()
    {
        await using VisualApp app = await StartAsync(t => t.ReadOnlyOnly = true);
        IPage page = await OpenAsync(app);
        string[] buttons = await Eval<string[]>(page, "[...document.querySelectorAll('.btn.try')].map(b => b.closest('section').id)");
        Assert.Contains("tool/echo", buttons);
        Assert.Contains("prompt/greet", buttons);
        Assert.Contains("template/thing", buttons);
        Assert.DoesNotContain("tool/rename", buttons);
        Assert.DoesNotContain("tool/wipe", buttons);
    }

    [Fact]
    public async Task A_session_token_survives_a_reload_and_Forget_clears_it()
    {
        IPage page = await OpenAsync();
        await TryAsync(page, "tool/echo");
        await page.FillAsync("#ti-token", "admin");
        await page.ReloadAsync();
        await page.WaitForSelectorAsync("main .prop-name");
        await TryAsync(page, "tool/wipe");
        Assert.Equal("admin", await page.InputValueAsync("#ti-token"));

        await page.ClickAsync("dialog.ti .ti-saved .btn");
        Assert.Equal("", await page.InputValueAsync("#ti-token"));
        Assert.Null(await Eval<string?>(page, "sessionStorage.getItem('mcp-docs-token:/mcp')"));
    }
}
