using System.Runtime.CompilerServices;
using System.Security.Claims;
using Xunit;

namespace Hlibz.McpDocs.AspNetCore.Tests;

/// <summary>
/// Contract snapshots: the catalog an anonymous caller and an admin get, checked in under <c>tests/Snapshots</c>.
/// A renamed parameter or a changed schema fails here; run with <c>UPDATE_SNAPSHOTS=1</c> to accept the change.
/// </summary>
public class SnapshotTests
{
    private static string Dir([CallerFilePath] string file = "") => Path.Combine(Path.GetDirectoryName(file)!, "Snapshots");

    private static ClaimsPrincipal Admin() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester"), new Claim(ClaimTypes.Role, "admin")], "Test"));

    private static async Task Verify(string name, ClaimsPrincipal? viewer)
    {
        await using var app = await TestApp.StartAsync();
        var actual = await McpDocsSnapshot.CreateAsync(app.App.Services, viewer: viewer);
        var path = Path.Combine(Dir(), name + ".json");

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
    public async Task Admin_sees_more_than_anonymous()
    {
        await using var app = await TestApp.StartAsync();
        var anon = await McpDocsSnapshot.CreateAsync(app.App.Services);
        var admin = await McpDocsSnapshot.CreateAsync(app.App.Services, viewer: Admin());
        Assert.DoesNotContain("delete_thing", anon);
        Assert.Contains("delete_thing", admin);
    }
}
