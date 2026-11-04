using System.ComponentModel;
using Hlibz.McpDocs.AspNetCore;
using ModelContextProtocol.Server;

namespace Sample.McpServer.Tools;

[McpServerToolType]
[McpDocsGroup("Utilities")]
public class UtilityTools
{
    [McpServerTool(Name = "echo", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Echoes the message back. The simplest possible tool, no title, no annotations beyond read-only.")]
    public string Echo([Description("Text to echo")] string message) => message;

    [McpServerTool(Name = "get_exchange_rate", Title = "Get exchange rate", ReadOnly = true, OpenWorld = true)]
    [Description("Looks up a live exchange rate from an external provider (open-world).")]
    public string GetExchangeRate(
        [Description("Base currency, ISO 4217")] string baseCurrency = "EUR",
        [Description("Quote currency, ISO 4217")] string quote = "UAH") =>
        $"1 {baseCurrency} = 48.12 {quote}";
}

public static class LambdaStyleTools
{
    // Registered at runtime in Program.cs (not via attributes): shows that the docs page lists runtime-registered tools too.
    public static string Ping() => "pong";
}
