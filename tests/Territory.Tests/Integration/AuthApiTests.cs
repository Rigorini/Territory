using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Territory.Api.Security;
using Territory.Application.Admins;
using Territory.Application.Auth;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class AuthApiTests(PostgresFixture fixture) : IAsyncLifetime
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

    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/admins")]
    public async Task ProtectedEndpoints_WithoutSession_Return401(string url)
    {
        var response = await NewClient().Http.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithoutAntiforgeryHeader_IsRejected()
    {
        await fixture.SeedAdminAsync("admin", Password);
        var client = NewClient();
        await client.Http.GetAsync("/api/auth/antiforgery", Ct);

        var response = await client.Http.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = Password }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AntiforgeryValidationFilter.ErrorCode, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401WithGenericCode()
    {
        await fixture.SeedAdminAsync("admin", Password);
        var client = NewClient();

        var response = await client.LoginAsync("admin", "Wrong-Password-1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.invalidCredentials", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task Login_SetsHardenedSessionCookie()
    {
        await fixture.SeedAdminAsync("admin", Password);

        var response = await NewClient().LoginAsync("admin", Password);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthSetup.AuthCookieName, StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FullFlow_LoginManageAdminsLogout()
    {
        var me = await fixture.SeedAdminAsync("admin", Password);
        var client = NewClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("admin", Password)).StatusCode);

        var profile = await client.Http.GetFromJsonAsync<JsonElement>("/api/auth/me", Ct);
        Assert.Equal("admin", profile.GetProperty("username").GetString());

        var created = await client.SendAsync(HttpMethod.Post, "/api/admins", new SaveAdminRequest("second", "Mario Rossi", Password));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = (await created.Content.ReadFromJsonAsync<AdminDto>(Ct))!;

        var updated = await client.SendAsync(HttpMethod.Put, $"/api/admins/{dto.Id}", new SaveAdminRequest("second-renamed", "Mario Rossi", null));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var list = await client.Http.GetFromJsonAsync<AdminDto[]>("/api/admins", Ct);
        Assert.Equal(["admin", "second-renamed"], list!.Select(a => a.Username));

        var deleteSelf = await client.SendAsync(HttpMethod.Delete, $"/api/admins/{me.Id}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, deleteSelf.StatusCode);
        Assert.Equal(AdminService.CannotDeleteSelf, await ReadCodeAsync(deleteSelf));

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Delete, $"/api/admins/{dto.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Post, "/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/auth/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task CreateAdmin_WithInvalidData_ReturnsValidationProblemWithCodes()
    {
        await fixture.SeedAdminAsync("admin", Password);
        var client = NewClient();
        await client.LoginAsync("admin", Password);

        var response = await client.SendAsync(HttpMethod.Post, "/api/admins", new SaveAdminRequest("x", "Mario Rossi", "weak"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("invalidFormat", body.GetProperty("errors").GetProperty("username")[0].GetString());
    }

    [Fact]
    public async Task PasswordChange_InvalidatesOtherSessions()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);
        var first = NewClient();
        var second = NewClient();
        await first.LoginAsync("admin", Password);
        await second.LoginAsync("admin", Password);

        var update = await first.SendAsync(HttpMethod.Put, $"/api/admins/{admin.Id}", new SaveAdminRequest("admin", "Mario Rossi", "New-Password-22"));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.Http.GetAsync("/api/auth/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task OwnPasswordChange_KeepsCurrentSessionAndInvalidatesOthers()
    {
        await fixture.SeedAdminAsync("admin", Password);
        var first = NewClient();
        var second = NewClient();
        await first.LoginAsync("admin", Password);
        await second.LoginAsync("admin", Password);

        var change = await first.SendAsync(HttpMethod.Put, "/api/auth/me/password", new ChangePasswordRequest(Password, "New-Password-22"));

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.Http.GetAsync("/api/auth/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.Http.GetAsync("/api/auth/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task AdminSettingAnotherUsersPassword_RequiresChangeAtNextLogin()
    {
        await fixture.SeedAdminAsync("admin", Password);
        var admin = NewClient();
        await admin.LoginAsync("admin", Password);
        var created = await admin.SendAsync(HttpMethod.Post, "/api/admins", new SaveAdminRequest("newbie", "Anna Verdi", Password));
        var dto = (await created.Content.ReadFromJsonAsync<AdminDto>(Ct))!;

        // The user replaces the initial temporary password and starts working normally.
        var user = NewClient();
        await user.LoginAsync("newbie", Password);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await user.SendAsync(HttpMethod.Put, "/api/auth/me/password", new ChangePasswordRequest(Password, "New-Password-22"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.Http.GetAsync("/api/admins", Ct)).StatusCode);

        // An administrator sets a new password: it is temporary again.
        var reset = await admin.SendAsync(HttpMethod.Put, $"/api/admins/{dto.Id}", new SaveAdminRequest("newbie", "Anna Verdi", "Reset-Password-33"));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.Http.GetAsync("/api/admins", Ct)).StatusCode);

        var back = NewClient();
        var login = await back.LoginAsync("newbie", "Reset-Password-33");
        Assert.True((await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("mustChangePassword").GetBoolean());
        var blocked = await back.Http.GetAsync("/api/admins", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(PasswordChangeRequiredFilter.ErrorCode, await ReadCodeAsync(blocked));
    }

    [Fact]
    public async Task CreatedUser_MustChangePasswordBeforeUsingTheApi()
    {
        await fixture.SeedAdminAsync("admin", Password);
        var admin = NewClient();
        await admin.LoginAsync("admin", Password);
        await admin.SendAsync(HttpMethod.Post, "/api/admins", new SaveAdminRequest("newbie", "Anna Verdi", Password));
        var client = NewClient();
        var login = await client.LoginAsync("newbie", Password);
        Assert.True((await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("mustChangePassword").GetBoolean());

        var blocked = await client.Http.GetAsync("/api/admins", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(PasswordChangeRequiredFilter.ErrorCode, await ReadCodeAsync(blocked));
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/auth/me", Ct)).StatusCode);

        var change = await client.SendAsync(HttpMethod.Put, "/api/auth/me/password", new ChangePasswordRequest(Password, "New-Password-22"));

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/admins", Ct)).StatusCode);
    }

    [Fact]
    public async Task CreatedUser_CanLogOutAndLogInAgainWithoutChangingThePassword()
    {
        await fixture.SeedAdminAsync("admin", Password);
        var admin = NewClient();
        await admin.LoginAsync("admin", Password);
        await admin.SendAsync(HttpMethod.Post, "/api/admins", new SaveAdminRequest("newbie", "Anna Verdi", Password));
        var client = NewClient();
        await client.LoginAsync("newbie", Password);

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Post, "/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/auth/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("newbie", Password)).StatusCode);
    }

    [Fact]
    public async Task OwnPasswordChange_WithWrongCurrentPassword_ReturnsFieldErrorAndKeepsSession()
    {
        await fixture.SeedAdminAsync("admin", Password);
        var client = NewClient();
        await client.LoginAsync("admin", Password);

        var change = await client.SendAsync(HttpMethod.Put, "/api/auth/me/password", new ChangePasswordRequest("Wrong-Password-1", "New-Password-22"));

        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        var body = await change.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(AuthService.WrongPassword, body.GetProperty("errors").GetProperty(AuthService.CurrentPasswordField)[0].GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/auth/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task OwnPasswordChange_IsAllowedToEveryRole()
    {
        await fixture.SeedAdminAsync("assigner", Password, Territory.Application.Domain.AdminRole.Assigner);
        var client = NewClient();
        await client.LoginAsync("assigner", Password);

        var change = await client.SendAsync(HttpMethod.Put, "/api/auth/me/password", new ChangePasswordRequest(Password, "New-Password-22"));

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
    }
    [Fact]
    public async Task Preferences_AreSavedAndReturnedByMe()
    {
        await fixture.SeedAdminAsync("admin", Password);
        var client = NewClient();
        await client.LoginAsync("admin", Password);

        var saved = await client.SendAsync(HttpMethod.Put, "/api/auth/me/preferences", new { theme = 2, language = "en" });
        var invalid = await client.SendAsync(HttpMethod.Put, "/api/auth/me/preferences", new { theme = 1, language = "de" });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var me = await client.Http.GetFromJsonAsync<JsonElement>("/api/auth/me", Ct);
        Assert.Equal((2, "en"), (me.GetProperty("theme").GetInt32(), me.GetProperty("language").GetString()));
    }

    [Fact]
    public async Task Preferences_WithoutSession_Return401()
    {
        var response = await NewClient().SendAsync(HttpMethod.Put, "/api/auth/me/preferences", new { theme = 2 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeletedAdmin_SessionIsRejected()
    {
        await fixture.SeedAdminAsync("admin", Password);
        var victim = await fixture.SeedAdminAsync("victim", Password);
        var adminClient = NewClient();
        var victimClient = NewClient();
        await adminClient.LoginAsync("admin", Password);
        await victimClient.LoginAsync("victim", Password);

        await adminClient.SendAsync(HttpMethod.Delete, $"/api/admins/{victim.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, (await victimClient.Http.GetAsync("/api/admins", Ct)).StatusCode);
    }

    private ApiTestClient NewClient() => ApiTestClient.Create(factory);

    private static Task<string?> ReadCodeAsync(HttpResponseMessage response) => ApiTestClient.ReadCodeAsync(response);
}
