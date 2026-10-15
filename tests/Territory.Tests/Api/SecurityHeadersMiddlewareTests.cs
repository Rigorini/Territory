using Microsoft.Extensions.Hosting;
using NSubstitute;
using Territory.Api.Security;

namespace Territory.Tests.Api;

public sealed class SecurityHeadersMiddlewareTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void PolicyFor_NonDevelopment_AllowsOnlySameOriginConnections(string environmentName)
    {
        var policy = SecurityHeadersMiddleware.PolicyFor(Environment(environmentName));

        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, policy);
        Assert.Contains("connect-src 'self';", policy);
        Assert.DoesNotContain("localhost", policy);
    }

    [Fact]
    public void PolicyFor_Development_AllowsLoopbackToolingOnlyInConnectSrc()
    {
        var policy = SecurityHeadersMiddleware.PolicyFor(Environment(Environments.Development));

        Assert.Contains("connect-src 'self' http://localhost:* https://localhost:* ws://localhost:* wss://localhost:*;", policy);
        Assert.Contains("script-src 'self';", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.Equal(
            SecurityHeadersMiddleware.ContentSecurityPolicy,
            policy.Replace(" http://localhost:* https://localhost:* ws://localhost:* wss://localhost:*", string.Empty, StringComparison.Ordinal));
    }

    private static IHostEnvironment Environment(string name)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }
}
