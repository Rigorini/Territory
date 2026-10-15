using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Territory.Application.Auth;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Security;
using Territory.Infrastructure.Persistence;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class AuthServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "Correct-Horse-1";
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero));
    private readonly List<TerritoryDbContext> contexts = [];
    private readonly CapturingLoggerFactory logs = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public async ValueTask DisposeAsync()
    {
        foreach (var context in contexts)
        {
            await context.DisposeAsync();
        }
    }

    private AuthService CreateService(IPasswordHasher<AdminUser>? hasher = null)
    {
        var db = fixture.CreateDbContext();
        contexts.Add(db);
        return new AuthService(db, hasher ?? new PasswordHasher<AdminUser>(), clock, new SecurityAuditLogger(logs));
    }

    [Fact]
    public async Task Login_WithValidCredentials_IsCaseInsensitiveOnUsername()
    {
        var admin = await fixture.SeedAdminAsync("Mario.Rossi", Password);

        var result = await CreateService().LoginAsync("  mario.rossi ", Password, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(admin.Id, result.Value!.Id);
    }

    [Theory]
    [InlineData(null, Password)]
    [InlineData("", Password)]
    [InlineData("admin", null)]
    [InlineData("admin", "")]
    [InlineData("unknown", Password)]
    [InlineData("admin", "Wrong-Password-1")]
    public async Task Login_WithInvalidInput_ReturnsGenericError(string? username, string? password)
    {
        await fixture.SeedAdminAsync("admin", Password);

        var result = await CreateService().LoginAsync(username, password, Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Unauthorized, result.Error!.Kind);
        Assert.Equal(AuthService.InvalidCredentials, result.Error.Code);
    }

    [Fact]
    public async Task Login_WithOverlongInput_IsRejectedWithoutDatabaseLookup()
    {
        var result = await CreateService().LoginAsync(new string('a', 51), new string('b', 129), Ct);

        Assert.Equal(AuthService.InvalidCredentials, result.Error!.Code);
    }

    [Fact]
    public async Task Login_AfterMaxFailures_LocksAccountEvenWithCorrectPassword()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);
        var service = CreateService();

        for (var i = 0; i < AuthService.MaxFailedAttempts; i++)
        {
            await service.LoginAsync("admin", "Wrong-Password-1", Ct);
        }

        var stored = await fixture.FindAdminAsync(admin.Id);
        Assert.Equal(clock.GetUtcNow() + AuthService.LockoutDuration, stored!.LockoutUntil);
        Assert.Equal(0, stored.FailedAttempts);
        Assert.False((await CreateService().LoginAsync("admin", Password, Ct)).IsSuccess);
    }

    [Fact]
    public async Task Login_AfterLockoutExpires_SucceedsAndResetsCounters()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);
        for (var i = 0; i < AuthService.MaxFailedAttempts; i++)
        {
            await CreateService().LoginAsync("admin", "Wrong-Password-1", Ct);
        }

        clock.Advance(AuthService.LockoutDuration + TimeSpan.FromSeconds(1));
        await CreateService().LoginAsync("admin", "Wrong-Password-1", Ct);
        var result = await CreateService().LoginAsync("admin", Password, Ct);

        Assert.True(result.IsSuccess);
        var stored = await fixture.FindAdminAsync(admin.Id);
        Assert.Equal(0, stored!.FailedAttempts);
        Assert.Null(stored.LockoutUntil);
    }

    [Fact]
    public async Task ChangePassword_WithCorrectCurrentPassword_RotatesHashAndStamp()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        var result = await CreateService().ChangePasswordAsync(admin.Id, new ChangePasswordRequest(Password, "New-Password-22"), Ct);

        Assert.True(result.IsSuccess);
        var stored = (await fixture.FindAdminAsync(admin.Id))!;
        Assert.NotEqual(admin.SecurityStamp, stored.SecurityStamp);
        Assert.True((await CreateService().LoginAsync("admin", "New-Password-22", Ct)).IsSuccess);
        Assert.False((await CreateService().LoginAsync("admin", Password, Ct)).IsSuccess);
    }

    [Theory]
    [InlineData(null, "New-Password-22", AuthService.CurrentPasswordField, "required")]
    [InlineData("", "New-Password-22", AuthService.CurrentPasswordField, "required")]
    [InlineData("Wrong-Password-1", "New-Password-22", AuthService.CurrentPasswordField, AuthService.WrongPassword)]
    public async Task ChangePassword_WithMissingOrWrongCurrentPassword_ReturnsFieldErrorAndKeepsPassword(
        string? current, string next, string field, string code)
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        var result = await CreateService().ChangePasswordAsync(admin.Id, new ChangePasswordRequest(current, next), Ct);

        Assert.Equal([code], result.Error!.Fields![field]);
        Assert.Equal(admin.PasswordHash, (await fixture.FindAdminAsync(admin.Id))!.PasswordHash);
    }

    [Fact]
    public async Task ChangePassword_WithWeakNewPassword_ReturnsValidation()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        var result = await CreateService().ChangePasswordAsync(admin.Id, new ChangePasswordRequest(Password, "weak"), Ct);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.True(result.Error.Fields!.ContainsKey("password"));
    }

    [Fact]
    public async Task ChangePassword_WrongAttempts_CountTowardsLockout()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);
        for (var i = 0; i < AuthService.MaxFailedAttempts; i++)
        {
            await CreateService().ChangePasswordAsync(admin.Id, new ChangePasswordRequest("Wrong-Password-1", "New-Password-22"), Ct);
        }

        var result = await CreateService().ChangePasswordAsync(admin.Id, new ChangePasswordRequest(Password, "New-Password-22"), Ct);

        Assert.Equal([AuthService.WrongPassword], result.Error!.Fields![AuthService.CurrentPasswordField]);
        Assert.Equal(clock.GetUtcNow() + AuthService.LockoutDuration, (await fixture.FindAdminAsync(admin.Id))!.LockoutUntil);
    }

    [Fact]
    public async Task ChangePassword_ForDeletedAdmin_IsUnauthorized()
    {
        var result = await CreateService().ChangePasswordAsync(42, new ChangePasswordRequest(Password, "New-Password-22"), Ct);

        Assert.Equal(AuthService.SessionExpired, result.Error!.Code);
    }
    [Fact]
    public async Task Login_WhenRehashNeeded_StoresNewHash()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);
        var hasher = new RehashingHasher();

        var result = await CreateService(hasher).LoginAsync("admin", Password, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(RehashingHasher.NewHash, (await fixture.FindAdminAsync(admin.Id))!.PasswordHash);
    }

    [Fact]
    public async Task GetSession_DependsOnSecurityStamp_AndReturnsCurrentState()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password, AdminRole.Manager);
        var service = CreateService();

        Assert.Equal(new SessionState(AdminRole.Manager, false), await service.GetSessionAsync(admin.Id, admin.SecurityStamp, Ct));
        Assert.Null(await service.GetSessionAsync(admin.Id, Guid.NewGuid(), Ct));
        Assert.Null(await service.GetSessionAsync(admin.Id + 1, admin.SecurityStamp, Ct));
    }

    [Fact]
    public async Task ChangePassword_ClearsTheMustChangeFlag()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);
        await using (var db = fixture.CreateDbContext())
        {
            (await db.AdminUsers.FindAsync([admin.Id], Ct))!.MustChangePassword = true;
            await db.SaveChangesAsync(Ct);
        }

        var result = await CreateService().ChangePasswordAsync(admin.Id, new ChangePasswordRequest(Password, "New-Password-22"), Ct);

        Assert.True(result.IsSuccess);
        Assert.False((await fixture.FindAdminAsync(admin.Id))!.MustChangePassword);
    }

    [Fact]
    public async Task GetProfile_ReturnsPreferencesOrNull()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);
        var service = CreateService();

        var profile = await service.GetProfileAsync(admin.Id, Ct);

        Assert.Equal(new AdminProfile(admin.Id, "admin", "Admin admin", ThemePreference.System, null, AdminRole.Administrator), profile);
        Assert.Null(await service.GetProfileAsync(admin.Id + 1, Ct));
    }

    [Theory]
    [InlineData(ThemePreference.Dark, "en")]
    [InlineData(ThemePreference.Light, "it")]
    [InlineData(ThemePreference.System, null)]
    public async Task UpdatePreferences_PersistsThemeAndLanguage(ThemePreference theme, string? language)
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        var result = await CreateService().UpdatePreferencesAsync(admin.Id, new PreferencesRequest(theme, language), Ct);

        Assert.Equal(new AdminProfile(admin.Id, "admin", "Admin admin", theme, language, AdminRole.Administrator), result.Value);
        var stored = (await fixture.FindAdminAsync(admin.Id))!;
        Assert.Equal((theme, language), (stored.Theme, stored.Language));
    }

    public static TheoryData<ThemePreference?, string?, string, string> InvalidPreferences => new()
    {
        { null, "it", AuthService.ThemeField, TextRules.Required },
        { (ThemePreference)7, "it", AuthService.ThemeField, TextRules.InvalidFormat },
        { ThemePreference.Dark, "fr", AuthService.LanguageField, TextRules.InvalidFormat },
        { ThemePreference.Dark, "IT", AuthService.LanguageField, TextRules.InvalidFormat }
    };

    [Theory]
    [MemberData(nameof(InvalidPreferences))]
    public async Task UpdatePreferences_RejectsInvalidValues(ThemePreference? theme, string? language, string field, string code)
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        var error = (await CreateService().UpdatePreferencesAsync(admin.Id, new PreferencesRequest(theme, language), Ct)).Error!;

        Assert.Equal(ErrorKind.Validation, error.Kind);
        Assert.Equal([code], error.Fields![field]);
        Assert.Equal(ThemePreference.System, (await fixture.FindAdminAsync(admin.Id))!.Theme);
    }

    [Fact]
    public async Task UpdatePreferences_ForDeletedAdmin_IsUnauthorized()
    {
        var error = (await CreateService().UpdatePreferencesAsync(999, new PreferencesRequest(ThemePreference.Dark, null), Ct)).Error!;

        Assert.Equal((ErrorKind.Unauthorized, AuthService.SessionExpired), (error.Kind, error.Code));
    }

    [Fact]
    public async Task Login_Success_IsAudited()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        await CreateService().LoginAsync("admin", Password, Ct);

        var entry = Assert.Single(logs.ForEvent(1001));
        Assert.Equal((SecurityAuditLogger.Category, LogLevel.Information), (entry.Category, entry.Level));
        Assert.Contains($"admin {admin.Id}", entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown", SecurityAuditLogger.ReasonUnknownUser)]
    [InlineData("admin", SecurityAuditLogger.ReasonWrongPassword)]
    public async Task Login_Failure_IsAuditedAsWarningWithoutCredentials(string username, string reason)
    {
        await fixture.SeedAdminAsync("admin", Password);

        await CreateService().LoginAsync(username, "Wrong-Password-1", Ct);

        var entry = Assert.Single(logs.ForEvent(1002));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains($"({reason})", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Wrong-Password-1", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lockout_IsAuditedOnceWithReasonLockedAfterwards()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        for (var i = 0; i < AuthService.MaxFailedAttempts; i++)
        {
            await CreateService().LoginAsync("admin", "Wrong-Password-1", Ct);
        }

        await CreateService().LoginAsync("admin", Password, Ct);

        var locked = Assert.Single(logs.ForEvent(1003));
        Assert.Contains($"Admin {admin.Id} locked out", locked.Message, StringComparison.Ordinal);
        Assert.Contains(logs.ForEvent(1002), e => e.Message.Contains($"({SecurityAuditLogger.ReasonLocked})", StringComparison.Ordinal));
    }

    private sealed class RehashingHasher : IPasswordHasher<AdminUser>
    {
        public const string NewHash = "rehashed";
        public string HashPassword(AdminUser user, string password) => NewHash;

        public PasswordVerificationResult VerifyHashedPassword(AdminUser user, string hashedPassword, string providedPassword) =>
            PasswordVerificationResult.SuccessRehashNeeded;
    }
}
