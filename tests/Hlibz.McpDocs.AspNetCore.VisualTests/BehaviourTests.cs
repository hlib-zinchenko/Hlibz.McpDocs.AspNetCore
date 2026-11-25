using System.ComponentModel;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

using ModelContextProtocol.Server;

using Xunit;

namespace Hlibz.McpDocs.AspNetCore.VisualTests;

/// <summary>
/// What app.js renders, checked in a real browser through the DOM rather than pixels, so it runs on any OS and in
/// CI. The Markdown renderer is the page's XSS boundary: descriptions come from the server's code and are shown
/// to anyone who opens the page.
/// </summary>
[Trait("Category", "Behaviour")]
public class BehaviourTests : IAsyncLifetime
{
    private VisualApp _app = null!;
    private IPlaywright _pw = null!;
    private IBrowser _browser = null!;

    // Payloads aimed at each step of the renderer: raw tags, code spans, link targets, attribute break-outs, emphasis.
    private const string Hostile = """
        **bold** and *em* and `<b>code</b>` and [link](https://example.com/a?b=1&c=2)

        - item <img src=x onerror="window.__xss=1">
        - [bad](javascript:window.__xss=1) and [data](data:text/html,<script>window.__xss=1</script>)

        <script>window.__xss=1</script> [x](https://e.example/" onmouseover="window.__xss=1) **<svg onload=window.__xss=1>**
        `` ` ``<iframe src="javascript:window.__xss=1"></iframe>` *<a href="javascript:window.__xss=1">em link</a>*
        """;

    public async ValueTask InitializeAsync()
    {
        _app = await VisualApp.StartAsync(
            server: s => s.WithTools([Hostile_tool(), AllOf_tool()]).WithTools<RecursiveTools>(),
            docs: o => { o.AuthHeader = "Authorization"; o.AuthHint = Hostile; });
        _pw = await Playwright.CreateAsync();
        _browser = await _pw.Chromium.LaunchAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _browser.DisposeAsync();
        _pw.Dispose();
        await _app.DisposeAsync();
    }

    private static McpServerTool Hostile_tool()
    {
        McpServerTool tool = McpServerTool.Create(([Description(Hostile)] string id) => id, new() { Name = "hostile", Description = Hostile });
        return tool;
    }

    private static McpServerTool AllOf_tool()
    {
        McpServerTool tool = McpServerTool.Create((string sku) => sku, new() { Name = "restock", Description = "Restocks a product." });
        tool.ProtocolTool.InputSchema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": { "item": { "$ref": "#/$defs/Item" } },
              "required": ["item"],
              "$defs": {
                "Base": { "type": "object", "properties": { "sku": { "type": "string", "description": "Product SKU" } }, "required": ["sku"] },
                "Item": {
                  "allOf": [
                    { "$ref": "#/$defs/Base" },
                    { "properties": { "quantity": { "type": "integer", "description": "How many" }, "next": { "$ref": "#/$defs/Item" } } }
                  ]
                }
              }
            }
            """).RootElement.Clone();
        return tool;
    }

    public record Node([property: Description("Node id")] string Id, IReadOnlyList<Node> Children, Node? Parent);
    public record Wrapper([property: Description("The tree")] Node Root);

    [McpServerToolType]
    public sealed class RecursiveTools
    {
        [McpServerTool(Name = "save_tree"), Description("Saves a tree.")]
        public string SaveTree([Description("The tree")] Wrapper tree) => tree.Root.Id;
    }

    private async Task<IPage> Open(string hash)
    {
        IPage page = await (await _browser.NewContextAsync()).NewPageAsync();
        List<string> errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        await page.GotoAsync($"{_app.BaseUrl}/mcp-docs/{hash}");
        await page.WaitForSelectorAsync("main .prop-name, main .md");
        Assert.Empty(errors);
        return page;
    }

    private static Task<T> Eval<T>(IPage page, string js) => page.EvaluateAsync<T>(js);

    // Every section is on the page; the hash only scrolls. This scopes a query to one.
    private static string In(string id, string selector) => $"[...document.getElementById('{id}').querySelectorAll('{selector}')]";

    [Fact]
    public async Task Markdown_renders_its_subset_and_nothing_else()
    {
        IPage page = await Open("#tool/hostile");
        // Tool description, parameter description and the auth hint all go through the renderer.
        Assert.True(await Eval<int>(page, "document.querySelectorAll('main .md').length") >= 2);

        Assert.Equal(0, await Eval<int>(page, "document.querySelectorAll('.md img, .md script, .md iframe, .md svg, .md object, .md embed, .md style').length"));
        Assert.Equal(0, await Eval<int>(page, "[...document.querySelectorAll('.md *')].filter(e => [...e.attributes].some(a => a.name.startsWith('on') || a.name === 'style' || a.name === 'src')).length"));
        Assert.Empty(await Eval<string[]>(page, "[...document.querySelectorAll('.md a')].map(a => a.getAttribute('href')).filter(h => !/^https:\\/\\//.test(h))"));
        Assert.Empty(await Eval<string[]>(page, "[...document.querySelectorAll('.md a')].map(a => a.rel).filter(r => r !== 'noopener noreferrer')"));
        Assert.False(await Eval<bool>(page, "'__xss' in window"));

        // The subset itself still works, and markup shows as text.
        Assert.Contains("bold", await Eval<string[]>(page, "[...document.querySelectorAll('main .md strong')].map(e => e.textContent)"));
        Assert.Contains("em", await Eval<string[]>(page, "[...document.querySelectorAll('main .md em')].map(e => e.textContent)"));
        Assert.Contains("<b>code</b>", await Eval<string[]>(page, "[...document.querySelectorAll('main .md code')].map(e => e.textContent)"));
        Assert.Contains("https://example.com/a?b=1&c=2", await Eval<string[]>(page, "[...document.querySelectorAll('main .md a')].map(a => a.href)"));
        Assert.True(await Eval<int>(page, "document.querySelectorAll('main .md ul > li').length") >= 2);
        Assert.Contains("<script>window.__xss=1</script>", await Eval<string>(page, "document.querySelector('main .md').textContent"));
    }

    [Fact]
    public async Task AllOf_parts_are_merged_into_one_object()
    {
        IPage page = await Open("#tool/restock");
        string[] names = await Eval<string[]>(page, In("tool/restock", ".l .prop-name") + ".map(e => e.textContent)");
        Assert.Equal(["item", "sku", "quantity", "next"], names);
        Assert.Equal("required", await Eval<string>(page, In("tool/restock", ".prop") + ".find(p => p.querySelector('.prop-name').textContent === 'sku').querySelector('.req')?.textContent ?? ''"));
        Assert.Equal(["↻ recursive: Item"], await Eval<string[]>(page, In("tool/restock", ".chip.recursive") + ".map(e => e.textContent)"));
        string request = await Eval<string>(page, In("tool/restock", ".r pre") + "[0].textContent");
        Assert.Contains("\"sku\": \"string\"", request);
        Assert.Contains("\"quantity\": 0", request);
    }

    [Fact]
    public async Task Recursion_chips_say_what_they_repeat()
    {
        IPage page = await Open("#tool/save_tree");
        // The SDK refers back by field pointer (#/properties/tree/properties/root/properties/children), so the chip names
        // the field the type first appeared as, not the field that repeats it.
        string[] chips = await Eval<string[]>(page, In("tool/save_tree", ".chip.recursive") + ".map(e => e.textContent)");
        Assert.Equal(["↻ recursive: same as tree.root.children[]", "↻ recursive: same as tree.root.children[].parent"], chips.Distinct().Order());
        Assert.Equal(6, chips.Length);
        // { "$ref": …, "items": {} }: the empty sibling must not hide the items the ref points to.
        Assert.DoesNotContain("array of any", await Eval<string[]>(page, In("tool/save_tree", ".prop-type") + ".map(e => e.textContent)"));
    }
}
