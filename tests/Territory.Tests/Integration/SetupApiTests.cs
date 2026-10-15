using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Territory.Application.Admins;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class SetupApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "Correct-Horse-1";
    private DbApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        factory = new DbApiFactory(fixture.ConnectionString);
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Status_IsRequired_OnlyWhileNoAdminExists()
    {
        var client = ApiTestClient.Create(factory);
        Assert.True(await IsRequiredAsync(client));

        await fixture.SeedAdminAsync("admin", Password);

        Assert.False(await IsRequiredAsync(client));
    }

    [Fact]
    public async Task Setup_CreatesFirstAdmin_WhoCanThenLogIn_AndOnlyOnce()
    {
        var client = ApiTestClient.Create(factory);
        await client.Http.GetAsync("/api/auth/antiforgery", Ct);

        var created = await client.SendAsync(HttpMethod.Post, "/api/setup", new { username = "mario", displayName = "Mario Rossi", password = Password });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("Mario Rossi", (await created.Content.ReadFromJsonAsync<AdminDto>(Ct))!.DisplayName);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("mario", Password)).StatusCode);

        var second = await client.SendAsync(HttpMethod.Post, "/api/setup", new { username = "other", displayName = "Other", password = Password });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("setup.alreadyDone", await ApiTestClient.ReadCodeAsync(second));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.AdminUsers.CountAsync(Ct));
    }

    [Fact]
    public async Task Setup_WithInvalidData_IsRejected_AndCreatesNothing()
    {
        var client = ApiTestClient.Create(factory);
        await client.Http.GetAsync("/api/auth/antiforgery", Ct);

        var response = await client.SendAsync(HttpMethod.Post, "/api/setup", new { username = "x", displayName = "", password = "weak" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await IsRequiredAsync(client));
    }

    [Fact]
    public async Task Setup_RunInParallel_CreatesExactlyOneAdmin()
    {
        var clients = Enumerable.Range(0, 5).Select(_ => ApiTestClient.Create(factory)).ToList();
        foreach (var c in clients)
        {
            await c.Http.GetAsync("/api/auth/antiforgery", Ct);
        }

        var responses = await Task.WhenAll(clients.Select((c, i) =>
            c.SendAsync(HttpMethod.Post, "/api/setup", new { username = $"admin{i}", displayName = $"Admin {i}", password = Password })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.AdminUsers.CountAsync(Ct));
    }

    private static async Task<bool> IsRequiredAsync(ApiTestClient client) =>
        (await client.Http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/setup/status", Ct)).GetProperty("required").GetBoolean();
}
