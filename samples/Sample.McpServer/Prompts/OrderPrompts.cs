using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Sample.McpServer.Prompts;

[McpServerPromptType]
public static class OrderPrompts
{
    [McpServerPrompt(Name = "summarize_order", Title = "Summarize order")]
    [Description("Asks the model to summarize an order for a customer-support reply.")]
    public static string SummarizeOrder(
        [Description("Order id")] string orderId,
        [Description("Tone: friendly or formal")] string tone = "friendly") =>
        $"Summarize order {orderId} in a {tone} tone for a support reply. Use get_order first.";

    [McpServerPrompt(Name = "weekly_report")]
    [Description("Builds a weekly sales report prompt.")]
    public static string WeeklyReport() =>
        "Produce a weekly sales report using search_orders for the last 7 days.";
}
