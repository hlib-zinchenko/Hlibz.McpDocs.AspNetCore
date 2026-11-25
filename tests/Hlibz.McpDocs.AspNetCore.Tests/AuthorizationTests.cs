using System.Net;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

public class AuthorizationTests
{
    [Theory]
    [InlineData("/mcp-docs/")]
    [InlineData("/mcp-docs/catalog.json")]
    [InlineData("/mcp-docs/_assets/app.js")]
    public async Task Protected_docs_reject_anonymous_callers_and_accept_signed_in_ones(string path)
    {
        await using TestApp app = await TestApp.StartAsync(requireAuth: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Client("user").GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Gated_tools_are_hidden_from_tools_list_for_anonymous_callers_too()
    {
        await using TestApp app = await TestApp.StartAsync();
        await using McpClient anon = await app.McpAsync();
        Assert.DoesNotContain(await anon.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken), t => t.Name == "delete_thing");
        await using McpClient admin = await app.McpAsync("admin");
        Assert.Contains(await admin.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken), t => t.Name == "delete_thing");
    }

    /// <summary>Listing a gated tool on the page (the default) changes nothing about who may call it.</summary>
    [Fact]
    public async Task Gated_tools_listed_on_the_page_are_still_refused_to_anonymous_callers()
    {
        await using TestApp app = await TestApp.StartAsync();
        Assert.Contains("delete_thing", await app.Client().GetStringAsync("/mcp-docs/catalog.json", TestContext.Current.CancellationToken));

        await using McpClient anon = await app.McpAsync();
        await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await anon.CallToolAsync("delete_thing", new Dictionary<string, object?> { ["id"] = "t-1" }, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// With GatedPrimitives = Hide the docs page decides visibility with its own copy of the SDK's authorization rules. This compares the two
    /// for every kind of metadata and viewer, so a newer SDK that filters differently (the weekly job runs these
    /// tests against the newest one) fails here instead of showing a viewer what their client wouldn't list.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("user")]
    [InlineData("admin")]
    [InlineData("owner")]
    [InlineData("editor")]
    [InlineData("auditor")]
    [InlineData("ops")]
    [InlineData("ops,oncall")]
    [InlineData("admin,editor,auditor,ops,oncall")]
    public async Task The_page_lists_exactly_what_the_server_lists_for_each_viewer(string? roles)
    {
        await using TestApp app = await TestApp.StartAsync(
            o => o.GatedPrimitives = McpDocsGatedPrimitives.Hide,
            server: s => s.WithTools<GatedTools>().WithPrompts<GatedPrompts>().WithResources<GatedResources>(),
            authorization: o => o.AddPolicy("Editors", p => p.RequireRole("editor")));

        JsonNode catalog = JsonNode.Parse(await app.Client(roles).GetStringAsync("/mcp-docs/catalog.json", TestContext.Current.CancellationToken))!;
        string[] Page(string section) => [.. catalog[section]!.AsArray().Select(e => (string)e!["definition"]!["name"]!).Order()];

        await using McpClient mcp = await app.McpAsync(roles);
        Assert.Equal([.. (await mcp.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(t => t.Name).Order()], Page("tools"));
        Assert.Equal([.. (await mcp.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(p => p.Name).Order()], Page("prompts"));
        Assert.Equal([.. (await mcp.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(r => r.Name).Order()], Page("resources"));
        Assert.Equal([.. (await mcp.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(r => r.Name).Order()], Page("resourceTemplates"));
    }

    /// <summary>A requirement without a policy name: <c>IAuthorizationRequirementData</c>, as with minimal APIs.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class AuditorsAttribute : Attribute, IAuthorizationRequirementData
    {
        public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new RolesAuthorizationRequirement(["auditor"])];
    }

    [McpServerToolType, Authorize]
    public sealed class GatedTools
    {
        [McpServerTool(Name = "signed_in")] public string SignedIn() => "";
        [McpServerTool(Name = "open_anyway"), AllowAnonymous] public string OpenAnyway() => "";
        [McpServerTool(Name = "admin_or_owner"), Authorize(Roles = "admin, owner")] public string AdminOrOwner() => "";
        [McpServerTool(Name = "ops_and_oncall"), Authorize(Roles = "ops"), Authorize(Roles = "oncall")] public string OpsAndOncall() => "";
        [McpServerTool(Name = "editors_policy"), Authorize(Policy = "Editors")] public string EditorsPolicy() => "";
        [McpServerTool(Name = "auditors_requirement"), Auditors] public string AuditorsRequirement() => "";
    }

    [McpServerPromptType]
    public sealed class GatedPrompts
    {
        [McpServerPrompt(Name = "admin_prompt"), Authorize(Roles = "admin")] public string AdminPrompt() => "";
        [McpServerPrompt(Name = "editor_prompt"), Authorize(Policy = "Editors")] public string EditorPrompt() => "";
    }

    [McpServerResourceType]
    public sealed class GatedResources
    {
        [McpServerResource(UriTemplate = "secret://admin", Name = "admin_resource"), Authorize(Roles = "admin")] public string Admin() => "";
        [McpServerResource(UriTemplate = "secret://audit/{id}", Name = "audit_template"), Auditors] public string Audit(string id) => id;
    }
}
