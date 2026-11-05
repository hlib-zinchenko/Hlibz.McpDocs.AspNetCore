using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>
/// Applies <c>[Authorize]</c> / <c>[AllowAnonymous]</c> on tools, prompts and resources the same way the SDK's
/// <c>AddAuthorizationFilters()</c> does for <c>tools/list</c> etc., so the docs page never shows a viewer
/// a primitive their MCP client wouldn't see. Mirrors ModelContextProtocol.AspNetCore 2.3.1
/// <c>AuthorizationFilterSetup</c>.
/// </summary>
internal static class McpDocsAuthorization
{
    /// <summary>Same rule as the SDK: any <see cref="IAllowAnonymous"/> wins, otherwise any authorization metadata gates.</summary>
    public static bool IsGated(IMcpServerPrimitive primitive) =>
        !primitive.Metadata.Any(static m => m is IAllowAnonymous) &&
        primitive.Metadata.Any(static m => m is IAuthorizeData or AuthorizationPolicy or IAuthorizationRequirementData);

    /// <summary>
    /// Evaluates the primitive's combined policy against <see cref="HttpContext.User"/>. The authorization resource is
    /// the <see cref="HttpContext"/> (the SDK passes its MCP request context, which doesn't exist here).
    /// </summary>
    public static async ValueTask<bool> IsAllowedAsync(HttpContext ctx, IMcpServerPrimitive primitive)
    {
        var services = ctx.RequestServices;
        var provider = services.GetService<IAuthorizationPolicyProvider>()
            ?? throw new InvalidOperationException(
                $"'{primitive.Id}' has authorization metadata, but authorization services aren't registered. Call AddAuthorization().");

        var policy = await CombineAsync(provider, primitive.Metadata);
        if (policy is null) return true;

        var result = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(ctx.User, ctx, policy);
        return result.Succeeded;
    }

    /// <summary>
    /// Writes what a viewer needs to know about a gated primitive: named policies, role sets
    /// (inner array = any of, outer = all of, as with several <c>[Authorize(Roles = …)]</c>), and whether there
    /// are requirements we can't name.
    /// </summary>
    public static void WriteRequirements(Utf8JsonWriter w, IMcpServerPrimitive primitive)
    {
        var data = primitive.Metadata.OfType<IAuthorizeData>().ToList();
        var policies = data.Select(d => d.Policy).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())
            .Distinct(StringComparer.Ordinal).ToList();
        var roleSets = data.Where(d => !string.IsNullOrWhiteSpace(d.Roles))
            .Select(d => d.Roles!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(r => r.Length > 0).ToList();
        var custom = primitive.Metadata.Any(static m => m is AuthorizationPolicy or IAuthorizationRequirementData);

        w.WriteStartObject("authorization");
        if (policies.Count > 0)
        {
            w.WriteStartArray("policies");
            foreach (var p in policies) w.WriteStringValue(p);
            w.WriteEndArray();
        }
        if (roleSets.Count > 0)
        {
            w.WriteStartArray("roles");
            foreach (var set in roleSets)
            {
                w.WriteStartArray();
                foreach (var r in set) w.WriteStringValue(r);
                w.WriteEndArray();
            }
            w.WriteEndArray();
        }
        if (custom) w.WriteBoolean("custom", true);
        w.WriteEndObject();
    }

    // Copied from ModelContextProtocol.AspNetCore's AuthorizationFilterSetup.CombineAsync (MIT), itself from
    // ASP.NET Core's AuthorizationMiddleware. https://github.com/dotnet/aspnetcore/issues/63365 tracks making it public.
    private static async ValueTask<AuthorizationPolicy?> CombineAsync(IAuthorizationPolicyProvider policyProvider, IReadOnlyList<object> metadata)
    {
        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, metadata.OfType<IAuthorizeData>(), metadata.OfType<AuthorizationPolicy>());

        AuthorizationPolicyBuilder? requirements = null;
        foreach (var m in metadata)
        {
            if (m is not IAuthorizationRequirementData data) continue;
            requirements ??= new AuthorizationPolicyBuilder();
            foreach (var r in data.GetRequirements()) requirements.AddRequirements(r);
        }

        if (requirements is null) return policy;
        return policy is null ? requirements.Build() : AuthorizationPolicy.Combine(policy, requirements.Build());
    }
}
