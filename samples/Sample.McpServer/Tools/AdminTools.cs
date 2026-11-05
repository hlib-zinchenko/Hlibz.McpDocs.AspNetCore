using System.ComponentModel;
using Hlibz.McpDocs.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Sample.McpServer.Tools;

/// <summary>
/// Gated with [Authorize]: hidden from tools/list and from the docs page unless the caller has the role.
/// Sign in at /demo/sign-in?roles=admin to see them.
/// </summary>
[McpServerToolType]
[McpDocsGroup("Admin")]
[Authorize(Roles = "admin")]
public class AdminTools
{
    [McpServerTool(Name = "refund_order", Title = "Refund order", Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Refunds an order to the original payment method. Admins only.")]
    public string RefundOrder([Description("Order id")] string orderId, [Description("Amount; full refund when omitted")] decimal? amount = null) =>
        $"Refunded {(amount is null ? "in full" : amount.ToString())} for {orderId}.";

    [McpServerTool(Name = "export_customers", Title = "Export customers", ReadOnly = true, OpenWorld = false)]
    [Description("Exports the customer list. Needs the `admin` role **and** the `Finance` policy.")]
    [Authorize(Policy = "Finance")]
    public string ExportCustomers() => "id,name\nC-1,Ada";

    [McpServerTool(Name = "get_service_status", Title = "Get service status", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Health of the order service. `[AllowAnonymous]` overrides the class-level `[Authorize]`.")]
    [AllowAnonymous]
    public string GetServiceStatus() => "ok";
}
