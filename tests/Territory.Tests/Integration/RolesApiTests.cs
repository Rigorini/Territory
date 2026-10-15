using System.Net;
using System.Net.Http.Json;
using Territory.Application.Admins;
using Territory.Application.Domain;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class RolesApiTests(PostgresFixture fixture) : IAsyncLifetime
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
    public async Task Assigner_CanOnlyWorkOnAssignments()
    {
        var client = await LoggedInAsync("assigner", AdminRole.Assigner);

        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/assignments", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/publishers", Ct)).StatusCode);
        await AssertForbiddenAsync(client, HttpMethod.Get, "/api/admins");
        await AssertForbiddenAsync(client, HttpMethod.Post, "/api/publishers", new { firstName = "A", lastName = "B" });
        await AssertForbiddenAsync(client, HttpMethod.Post, "/api/territories", new { });
        await AssertForbiddenAsync(client, HttpMethod.Post, "/api/municipalities", new { });
        await AssertForbiddenAsync(client, HttpMethod.Get, "/api/backup/export");
        await AssertForbiddenAsync(client, HttpMethod.Get, "/api/export/register");
        await AssertForbiddenAsync(client, HttpMethod.Post, "/api/backup/import?confirmOverwrite=true");
    }

    [Fact]
    public async Task Manager_CanManagePublishers_AndExport_ButNotImport()
    {
        var client = await LoggedInAsync("manager", AdminRole.Manager);

        var created = await client.SendAsync(HttpMethod.Post, "/api/publishers", new { firstName = "Mario", lastName = "Rossi" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/backup/export", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/assignments", Ct)).StatusCode);
        await AssertForbiddenAsync(client, HttpMethod.Post, "/api/backup/import?confirmOverwrite=true");
        await AssertForbiddenAsync(client, HttpMethod.Get, "/api/admins");
        await AssertForbiddenAsync(client, HttpMethod.Post, "/api/territories", new { });
        await AssertForbiddenAsync(client, HttpMethod.Post, "/api/municipalities", new { });
    }

    [Fact]
    public async Task Administrator_IsNotForbiddenAnywhere()
    {
        var client = await LoggedInAsync("admin", AdminRole.Administrator);

        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/admins", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/backup/export", Ct)).StatusCode);
        var import = await client.SendAsync(HttpMethod.Post, "/api/backup/import?confirmOverwrite=true");
        Assert.NotEqual(HttpStatusCode.Forbidden, import.StatusCode);
    }

    [Fact]
    public async Task RoleChange_AppliesToTheOpenSession()
    {
        var manager = await fixture.SeedAdminAsync("manager", Password, AdminRole.Manager);
        var client = ApiTestClient.Create(factory);
        await client.LoginAsync("manager", Password);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/backup/export", Ct)).StatusCode);

        await using (var db = fixture.CreateDbContext())
        {
            var user = await db.AdminUsers.FindAsync([manager.Id], Ct);
            user!.Role = AdminRole.Assigner;
            await db.SaveChangesAsync(Ct);
        }

        await AssertForbiddenAsync(client, HttpMethod.Get, "/api/backup/export");
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/assignments", Ct)).StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateAndChangeTheRoleOfAnotherUser()
    {
        var client = await LoggedInAsync("admin", AdminRole.Administrator);

        var created = await client.SendAsync(HttpMethod.Post, "/api/admins",
            new { username = "marta", displayName = "Marta Bianchi", password = Password, role = (int)AdminRole.Assigner });
        var dto = (await created.Content.ReadFromJsonAsync<AdminDto>(Ct))!;
        var updated = await client.SendAsync(HttpMethod.Put, $"/api/admins/{dto.Id}",
            new { username = "marta", displayName = "Marta Bianchi", role = (int)AdminRole.Manager });

        Assert.Equal(AdminRole.Assigner, dto.Role);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(AdminRole.Manager, (await fixture.FindAdminAsync(dto.Id))!.Role);
    }

    [Fact]
    public async Task Admin_CannotChangeOwnRole_NorUseAnUnknownRole()
    {
        var self = await fixture.SeedAdminAsync("admin", Password);
        var client = ApiTestClient.Create(factory);
        await client.LoginAsync("admin", Password);

        var own = await client.SendAsync(HttpMethod.Put, $"/api/admins/{self.Id}",
            new { username = "admin", displayName = "Admin", role = (int)AdminRole.Assigner });
        var unknown = await client.SendAsync(HttpMethod.Post, "/api/admins",
            new { username = "marta", displayName = "Marta", password = Password, role = 99 });

        Assert.Equal("admins.cannotChangeOwnRole", await ApiTestClient.ReadCodeAsync(own));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(AdminRole.Administrator, (await fixture.FindAdminAsync(self.Id))!.Role);
    }

    [Fact]
    public async Task Setup_AlwaysCreatesAnAdministrator_EvenIfARoleIsSent()
    {
        var client = ApiTestClient.Create(factory);
        await client.Http.GetAsync("/api/auth/antiforgery", Ct);

        var response = await client.SendAsync(HttpMethod.Post, "/api/setup",
            new { username = "mario", displayName = "Mario Rossi", password = Password, role = (int)AdminRole.Assigner });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AdminRole.Administrator, (await response.Content.ReadFromJsonAsync<AdminDto>(Ct))!.Role);
    }

    private async Task<ApiTestClient> LoggedInAsync(string username, AdminRole role)
    {
        await fixture.SeedAdminAsync(username, Password, role);
        var client = ApiTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(username, Password)).StatusCode);
        return client;
    }

    private static async Task AssertForbiddenAsync(ApiTestClient client, HttpMethod method, string url, object? body = null) =>
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(method, url, body)).StatusCode);
}
