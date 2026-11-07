using Microsoft.Playwright;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace Hlibz.McpDocs.AspNetCore.VisualTests;

/// <summary>
/// Screenshots of the page against checked-in baselines (per OS: font rendering differs). A missing baseline
/// is written and the test passes; <c>UPDATE_SNAPSHOTS=1</c> accepts changes. Needs Chromium:
/// <c>pwsh tests/Visual/bin/Debug/net8.0/playwright.ps1 install chromium</c>.
/// </summary>
public class VisualTests : IAsyncLifetime
{
    // Per-channel difference below this is anti-aliasing noise; a fraction of pixels above this is a real change.
    private const int ChannelTolerance = 12;
    private const double MaxChangedFraction = 0.002;

    private VisualApp _app = null!;
    private IPlaywright _pw = null!;
    private IBrowser _browser = null!;

    public async Task InitializeAsync()
    {
        _app = await VisualApp.StartAsync();
        _pw = await Playwright.CreateAsync();
        _browser = await _pw.Chromium.LaunchAsync();
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _pw.Dispose();
        await _app.DisposeAsync();
    }

    private static string Os => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "linux";
    private static string Dir([CallerFilePath] string file = "") => Path.Combine(Path.GetDirectoryName(file)!, "Baselines", Os);

    // Returns [actualW, actualH, baselineW, baselineH, changedPixels].
    private const string CompareJs = """
        async ([a, b, tol]) => {
          const load = async (b64) => {
            const img = new Image();
            img.src = 'data:image/png;base64,' + b64;
            await img.decode();
            const c = new OffscreenCanvas(img.width, img.height).getContext('2d');
            c.drawImage(img, 0, 0);
            return c.getImageData(0, 0, img.width, img.height);
          };
          const x = await load(a), y = await load(b);
          if (x.width !== y.width || x.height !== y.height) return [x.width, x.height, y.width, y.height, 0];
          let changed = 0;
          for (let i = 0; i < x.data.length; i += 4)
            if (Math.abs(x.data[i] - y.data[i]) > tol || Math.abs(x.data[i + 1] - y.data[i + 1]) > tol || Math.abs(x.data[i + 2] - y.data[i + 2]) > tol) changed++;
          return [x.width, x.height, y.width, y.height, changed];
        }
        """;

    private async Task Check(string name, string hash, ColorScheme scheme, int width, int height)
    {
        await using var ctx = await _browser.NewContextAsync(new()
        {
            ColorScheme = scheme,
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await ctx.NewPageAsync();
        await page.GotoAsync($"{_app.BaseUrl}/mcp-docs/{hash}");
        await page.WaitForSelectorAsync("main :first-child");
        await page.EvaluateAsync("document.fonts.ready");
        await page.WaitForTimeoutAsync(300);
        var actual = await page.ScreenshotAsync(new() { Animations = ScreenshotAnimations.Disabled });

        var path = Path.Combine(Dir(), name + ".png");
        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Dir());
            await File.WriteAllBytesAsync(path, actual);
            return;
        }

        // Decode and compare in the browser (canvas) on a blank page, so the docs page's CSP doesn't apply
        // and the tests need no image library.
        var blank = await ctx.NewPageAsync();
        var r = await blank.EvaluateAsync<int[]>(CompareJs, new object[] { Convert.ToBase64String(actual), Convert.ToBase64String(await File.ReadAllBytesAsync(path)), ChannelTolerance });
        Assert.True(r[0] == r[2] && r[1] == r[3], $"{name}: size {r[0]}x{r[1]} != baseline {r[2]}x{r[3]}");
        long changed = r[4];
        var fraction = (double)changed / ((long)r[0] * r[1]);
        if (fraction > MaxChangedFraction)
        {
            var diff = Path.Combine(Path.GetTempPath(), name + ".actual.png");
            await File.WriteAllBytesAsync(diff, actual);
            Assert.Fail($"{name}: {fraction:P2} of pixels changed. Actual saved to {diff}; UPDATE_SNAPSHOTS=1 accepts it.");
        }
    }

    [Fact] public Task Overview_light() => Check("overview-light", "", ColorScheme.Light, 1280, 800);
    [Fact] public Task Overview_dark() => Check("overview-dark", "", ColorScheme.Dark, 1280, 800);
    [Fact] public Task Overview_mobile() => Check("overview-mobile", "", ColorScheme.Light, 390, 800);
    [Fact] public Task Recursive_schema_light() => Check("recursive-light", "#tool/get_category_tree", ColorScheme.Light, 1280, 900);
    [Fact] public Task Recursive_schema_dark() => Check("recursive-dark", "#tool/get_category_tree", ColorScheme.Dark, 1280, 900);
    [Fact] public Task Polymorphic_schema_mobile() => Check("polymorphic-mobile", "#tool/apply_discount", ColorScheme.Light, 390, 900);
}
