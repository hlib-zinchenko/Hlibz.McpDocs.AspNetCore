using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Hlibz.McpDocs.AspNetCore.VisualTests;

/// <summary>A real Kestrel server on a free port, so a browser can load the page.</summary>
internal sealed class VisualApp : IAsyncDisposable
{
    private WebApplication _app = null!;
    public string BaseUrl { get; private set; } = "";

    public static async Task<VisualApp> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "visual-server", Title = "Visual Server", Version = "1.0.0" })
            .WithHttpTransport().WithTools<Tools>().WithPrompts<Prompts>().WithResources<Resources>();

        var app = builder.Build();
        app.MapMcp("/mcp");
        app.MapMcpDocs("/mcp-docs", o => o.AddToolbarItem("GitHub", "https://example.com"));
        await app.StartAsync();
        return new VisualApp { _app = app, BaseUrl = app.Urls.First() };
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    public record Category(
        [property: Description("Category id")] string Id,
        [property: Description("Child categories (recursive)")] IReadOnlyList<Category> Children);

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(Percent), "percent")]
    [JsonDerivedType(typeof(Fixed), "fixed")]
    public abstract record Discount;
    public record Percent([property: Description("Percent off")] int Value) : Discount;
    public record Fixed([property: Description("Amount off")] decimal Amount) : Discount;

    [McpServerToolType]
    public sealed class Tools
    {
        [McpServerTool(Name = "get_category_tree", ReadOnly = true, UseStructuredContent = true), Description("Returns the category tree. The output type is recursive.")]
        public Category Tree() => new("root", []);

        [McpServerTool(Name = "apply_discount", Destructive = false), Description("Applies a discount.\n\n- Percent or fixed\n- Uses `anyOf`")]
        public string Apply([Description("Product SKU")] string sku, [Description("The discount")] Discount discount) => sku;
    }

    [McpServerPromptType]
    public sealed class Prompts
    {
        [McpServerPrompt(Name = "greet"), Description("Greets someone.")]
        public string Greet([Description("Who")] string name) => "Hello " + name;
    }

    [McpServerResourceType]
    public sealed class Resources
    {
        [McpServerResource(UriTemplate = "docs://readme", Name = "readme"), Description("The readme.")]
        public string Readme() => "readme";
    }
}
