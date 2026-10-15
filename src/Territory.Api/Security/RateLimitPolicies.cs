using System.Threading.RateLimiting;
using Territory.Api.Logging;

namespace Territory.Api.Security;

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string PublicToken = "public-token";

    public static IServiceCollection AddTerritoryRateLimiting(this IServiceCollection services)
    {
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                HttpSecurityAudit.RateLimitExceeded(context.HttpContext);
                return ValueTask.CompletedTask;
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                FixedWindow(ClientKey(ctx), permitLimit: 300, TimeSpan.FromMinutes(1)));
            options.AddPolicy(Login, ctx => FixedWindow(ClientKey(ctx), permitLimit: 10, TimeSpan.FromMinutes(5)));
            options.AddPolicy(PublicToken, ctx => FixedWindow(ClientKey(ctx), permitLimit: 30, TimeSpan.FromMinutes(1)));
        });
    }

    // Per-replica limits: acceptable for brute-force mitigation; account lockout in DB is the authoritative control.
    private static RateLimitPartition<string> FixedWindow(string key, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0
        });

    private static string ClientKey(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
