using System.Runtime.CompilerServices;
using System.Security.Claims;

using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

/// <summary>
/// Contract snapshots: the catalog an anonymous caller and an admin get with <c>GatedPrimitives = Hide</c>, checked in
/// under <c>Snapshots</c>.
/// A renamed parameter or a changed schema fails here; run with <c>UPDATE_SNAPSHOTS=1</c> to accept the change.
/// </summary>
public class SnapshotTests
{
    private static string Dir([CallerFilePath] string file = "") => Path.Combine(Path.GetDirectoryName(file)!, "Snapshots");

    private static ClaimsPrincipal Admin() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester"), new Claim(ClaimTypes.Role, "admin")], "Test"));

    private static async Task Verify(string name, ClaimsPrincipal? viewer)
    {
        await using TestApp app = await TestApp.StartAsync();
        string actual = await McpDocsSnapshot.CreateAsync(app.App.Services, o => o.GatedPrimitives = McpDocsGatedPrimitives.Hide, viewer: viewer);
        string path = Path.Combine(Dir(), name + ".json");

        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Dir());
            await File.WriteAllTextAsync(path, actual);
            return;
        }
        Assert.True(await File.ReadAllTextAsync(path) == actual,
            $"The MCP contract changed ({name}). Review the diff, then re-run with UPDATE_SNAPSHOTS=1 to accept it.");
    }

    [Fact] public Task Anonymous_contract_is_unchanged() => Verify("anonymous", null);
    [Fact] public Task Admin_contract_is_unchanged() => Verify("admin", Admin());

    [Fact]
    public async Task Presentation_is_left_out_unless_asked_for()
    {
        await using TestApp app = await TestApp.StartAsync();
        void Docs(McpDocsOptions o) { o.Title = "Renamed"; o.AddToolbarItem("Status", "https://status.example"); o.ToolGroup = _ => "Things"; }
        Assert.Equal(await McpDocsSnapshot.CreateAsync(app.App.Services), await McpDocsSnapshot.CreateAsync(app.App.Services, Docs));

        string full = await McpDocsSnapshot.CreateAsync(app.App.Services, Docs, includePresentation: true);
        Assert.Contains("\"title\": \"Renamed\"", full);
        Assert.Contains("\"group\": \"Things\"", full);
        Assert.Contains("\"issues\"", full);
    }

    [Fact]
    public async Task With_Hide_admin_sees_more_than_anonymous()
    {
        await using TestApp app = await TestApp.StartAsync();
        string anon = await McpDocsSnapshot.CreateAsync(app.App.Services, o => o.GatedPrimitives = McpDocsGatedPrimitives.Hide);
        string admin = await McpDocsSnapshot.CreateAsync(app.App.Services, o => o.GatedPrimitives = McpDocsGatedPrimitives.Hide, viewer: Admin());
        Assert.DoesNotContain("delete_thing", anon);
        Assert.Contains("delete_thing", admin);
    }

    [Fact]
    public async Task By_default_every_viewer_gets_the_same_contract_with_gated_primitives()
    {
        await using TestApp app = await TestApp.StartAsync();
        string anon = await McpDocsSnapshot.CreateAsync(app.App.Services);
        Assert.Contains("delete_thing", anon);
        Assert.Equal(anon, await McpDocsSnapshot.CreateAsync(app.App.Services, viewer: Admin()));
    }
}
