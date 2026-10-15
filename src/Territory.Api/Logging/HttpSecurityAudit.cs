using Microsoft.AspNetCore.Routing;
using Territory.Api.Security;
using Territory.Application.Security;

namespace Territory.Api.Logging;

/// <summary>Writes HTTP-level security events; the route template is logged instead of the raw, client-controlled path.</summary>
public static class HttpSecurityAudit
{
    public static void AccessDenied(HttpContext context) =>
        Audit(context).AccessDenied(context.User.GetAdminId(), context.Request.Method, RouteOf(context));

    public static void RateLimitExceeded(HttpContext context) =>
        Audit(context).RateLimitExceeded(context.Request.Method, RouteOf(context));

    public static void AntiforgeryValidationFailed(HttpContext context) =>
        Audit(context).AntiforgeryValidationFailed(context.User.GetAdminId(), context.Request.Method, RouteOf(context));

    public static string? RouteOf(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;

    private static SecurityAuditLogger Audit(HttpContext context) =>
        context.RequestServices.GetRequiredService<SecurityAuditLogger>();
}
