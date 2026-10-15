using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Territory.Api.Logging;
using Territory.Api.Security;
using Territory.Application.Domain;
using Territory.Application.Security;

namespace Territory.Tests.Api;

public sealed class HttpSecurityAuditTests
{
    private readonly CapturingLoggerFactory logs = new();

    [Fact]
    public void AccessDenied_LogsAdminMethodAndRouteTemplate()
    {
        var context = Context("/api/admins/{id}");
        context.User = AuthSetup.CreatePrincipal(new AdminUser { Id = 7, Username = "admin", DisplayName = "Admin", PasswordHash = "hash", SecurityStamp = Guid.NewGuid() });

        HttpSecurityAudit.AccessDenied(context);

        var entry = Assert.Single(logs.ForEvent(1010));
        Assert.Equal((SecurityAuditLogger.Category, LogLevel.Warning), (entry.Category, entry.Level));
        Assert.Equal("Access denied to admin 7 on DELETE /api/admins/{id}", entry.Message);
    }

    [Fact]
    public void RouteOf_WithoutEndpoint_IsNull()
    {
        var context = new DefaultHttpContext();

        Assert.Null(HttpSecurityAudit.RouteOf(context));
    }

    private DefaultHttpContext Context(string route)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(new SecurityAuditLogger(logs)).BuildServiceProvider()
        };
        context.Request.Method = HttpMethods.Delete;
        context.Request.Path = "/api/admins/7";
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(route), 0, null, null));
        return context;
    }
}
