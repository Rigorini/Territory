using System.Net;
using Territory.Api.Security;
using Territory.Tests.Integration;

namespace Territory.Tests.Api;

[Collection(DatabaseCollection.Name)]
public sealed class PipelineTests(PostgresFixture fixture) : IAsyncLifetime
{
    private DbApiFactory factory = null!;

    public ValueTask InitializeAsync()
    {
        factory = new DbApiFactory(fixture.ConnectionString);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task ApiResponse_ContainsSecurityHeaders()
    {
        var response = await factory.CreateClient().GetAsync("/api/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal(SecurityHeadersMiddleware.DevelopmentContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task UnknownApiRoute_Returns404InsteadOfSpa()
    {
        var response = await factory.CreateClient().GetAsync("/api/does-not-exist", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LivenessProbe_DoesNotDependOnDatabase()
    {
        var response = await factory.CreateClient().GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
