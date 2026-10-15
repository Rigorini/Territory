using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Territory.Api.Logging;
using Territory.Application.Security;

namespace Territory.Tests.Integration;

/// <summary>End-to-end checks on the daily log file written by the real pipeline.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SecurityAuditApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "Correct-Horse-1";
    private DbApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        factory = new DbApiFactory(fixture.ConnectionString, fileLogging: true);
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task LoginAndLogout_AreWrittenToFile_WithClientIpAndWithoutSecrets()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);
        var client = ApiTestClient.Create(factory);

        await client.LoginAsync("admin", Password);
        await client.SendAsync(HttpMethod.Post, "/api/auth/logout");

        var entries = await ReadEntriesAsync();
        var login = Assert.Single(entries, e => EventId(e) == 1001);
        Assert.Equal($"Login succeeded for admin {admin.Id}", login.GetProperty("@m").GetString());
        Assert.True(login.TryGetProperty(RequestLogContextMiddleware.ClientIpProperty, out _));
        Assert.Single(entries, e => EventId(e) == 1004);
        Assert.DoesNotContain(Password, await factory.ReadLogAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task File_ContainsOnlyWarningsAndAuditEvents()
    {
        await fixture.SeedAdminAsync("admin", Password);
        var client = ApiTestClient.Create(factory);
        await client.LoginAsync("admin", Password);
        await client.Http.GetAsync("/api/admins", Ct);

        var entries = await ReadEntriesAsync();

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.True(IsWarningOrAbove(e) || SourceContext(e) == SecurityAuditLogger.Category));
    }

    [Fact]
    public async Task RevokedSession_IsAudited()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);
        var client = ApiTestClient.Create(factory);
        await client.LoginAsync("admin", Password);
        await using (var db = fixture.CreateDbContext())
        {
            await db.AdminUsers.Where(u => u.Id == admin.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, Guid.NewGuid()), Ct);
        }

        var response = await client.Http.GetAsync("/api/auth/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var rejected = Assert.Single(await ReadEntriesAsync(), e => EventId(e) == 1005);
        Assert.Equal($"Session rejected (\"{SecurityAuditLogger.ReasonRevoked}\") for admin {admin.Id}", rejected.GetProperty("@m").GetString());
    }

    [Fact]
    public async Task AntiforgeryFailure_IsAuditedWithRouteTemplate()
    {
        var client = ApiTestClient.Create(factory);

        await client.Http.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = Password }, Ct);

        var entry = Assert.Single(await ReadEntriesAsync(), e => EventId(e) == 1012);
        Assert.Equal("Warning", entry.GetProperty("@l").GetString());
        Assert.Equal("/api/auth/login", entry.GetProperty("Route").GetString());
    }

    [Fact]
    public async Task RateLimitRejection_IsAudited()
    {
        var client = ApiTestClient.Create(factory);
        var status = HttpStatusCode.OK;
        for (var i = 0; i < 20 && status != HttpStatusCode.TooManyRequests; i++)
        {
            status = (await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { username = "x", password = "y" })).StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, status);
        Assert.Contains(await ReadEntriesAsync(), e => EventId(e) == 1011);
    }

    private async Task<IReadOnlyList<JsonElement>> ReadEntriesAsync()
    {
        var content = await factory.ReadLogAsync();
        return [.. content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())];
    }

    private static int? EventId(JsonElement entry) =>
        entry.TryGetProperty("EventId", out var id) && id.TryGetProperty("Id", out var value) ? value.GetInt32() : null;

    private static string? SourceContext(JsonElement entry) =>
        entry.TryGetProperty("SourceContext", out var source) ? source.GetString() : null;

    // Compact JSON omits "@l" for Information.
    private static bool IsWarningOrAbove(JsonElement entry) =>
        entry.TryGetProperty("@l", out var level) && level.GetString() is "Warning" or "Error" or "Fatal";
}
