using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Time.Testing;
using Territory.Application.Admins;
using Territory.Application.Auth;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Security;
using Territory.Infrastructure.Persistence;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class AdminServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "Correct-Horse-1";
    private const string NewPassword = "New-Password-22";
    private const int ActorId = 999;
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero));
    private readonly CapturingLoggerFactory logs = new();
    private TerritoryDbContext db = null!;
    private AdminService service = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        db = fixture.CreateDbContext();
        var hasher = new PasswordHasher<AdminUser>();
        var audit = new SecurityAuditLogger(logs);
        service = new AdminService(db, hasher, clock, audit);
    }

    public ValueTask DisposeAsync() => db.DisposeAsync();

    [Fact]
    public async Task Create_PersistsHashedPassword_AndListsSortedByName()
    {
        var created = await service.CreateAsync(new SaveAdminRequest(" zeta ", " Anna Verdi ", Password), ActorId, Ct);
        await service.CreateAsync(new SaveAdminRequest("alfa", "Mario Rossi", Password), ActorId, Ct);

        Assert.True(created.IsSuccess);
        Assert.Equal("zeta", created.Value!.Username);
        Assert.Equal("Anna Verdi", created.Value.DisplayName);
        var stored = await fixture.FindAdminAsync(created.Value.Id);
        Assert.NotEqual(Password, stored!.PasswordHash);
        Assert.Equal("Anna Verdi", stored.DisplayName);
        Assert.True(stored.MustChangePassword);
        Assert.Equal(["zeta", "alfa"], (await service.ListAsync(Ct)).Select(a => a.Username));
        Assert.Equal(2, logs.ForEvent(1007).Count);
        Assert.Contains(logs.ForEvent(1007), e => e.Message == $"Admin {created.Value.Id} created by {ActorId}");
    }

    [Fact]
    public async Task Create_WithInvalidData_ReturnsFieldCodes()
    {
        var result = await service.CreateAsync(new SaveAdminRequest("a b", "Mario Rossi", "short"), ActorId, Ct);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(["invalidFormat"], result.Error.Fields![AdminRules.UsernameField]);
        Assert.Contains("tooShort", result.Error.Fields[AdminRules.PasswordField]);
    }

    [Theory]
    [InlineData(null, TextRules.Required)]
    [InlineData("   ", TextRules.Required)]
    [InlineData("Mario\tRossi", TextRules.InvalidFormat)]
    public async Task Create_And_Update_ValidateDisplayName(string? displayName, string code)
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        var created = await service.CreateAsync(new SaveAdminRequest("other", displayName, Password), ActorId, Ct);
        var updated = await service.UpdateAsync(admin.Id, new SaveAdminRequest("admin", displayName, null), ActorId, Ct);

        Assert.Equal([code], created.Error!.Fields![AdminRules.DisplayNameField]);
        Assert.Equal([code], updated.Error!.Fields![AdminRules.DisplayNameField]);
    }

    [Fact]
    public async Task Create_WithTooLongDisplayName_ReturnsMaxLength()
    {
        var result = await service.CreateAsync(new SaveAdminRequest("other", new string('a', AdminUser.DisplayNameMaxLength + 1), Password), ActorId, Ct);

        Assert.Equal([TextRules.MaxLength], result.Error!.Fields![AdminRules.DisplayNameField]);
    }

    [Fact]
    public async Task Create_WithMissingUsername_ReturnsRequired()
    {
        var result = await service.CreateAsync(new SaveAdminRequest("  ", "Mario Rossi", Password), ActorId, Ct);

        Assert.Equal(["required"], result.Error!.Fields![AdminRules.UsernameField]);
    }

    [Fact]
    public async Task Create_WithExistingUsernameDifferentCase_ReturnsConflict()
    {
        await fixture.SeedAdminAsync("Admin", Password);

        var result = await service.CreateAsync(new SaveAdminRequest("ADMIN", "Mario Rossi", Password), ActorId, Ct);

        Assert.Equal(AdminService.UsernameTaken, result.Error!.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
    }

    [Fact]
    public async Task Update_WithoutPassword_KeepsHashAndStamp()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        var result = await service.UpdateAsync(admin.Id, new SaveAdminRequest("renamed", "Luca Bianchi", null), ActorId, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("Luca Bianchi", result.Value!.DisplayName);
        var stored = await fixture.FindAdminAsync(admin.Id);
        Assert.Equal("renamed", stored!.Username);
        Assert.Equal("Luca Bianchi", stored.DisplayName);
        Assert.Equal(admin.PasswordHash, stored.PasswordHash);
        Assert.Equal(admin.SecurityStamp, stored.SecurityStamp);
    }

    [Fact]
    public async Task Update_WithPassword_RotatesStampAndClearsLockout()
    {
        var actor = await fixture.SeedAdminAsync("actor", Password);
        var admin = await fixture.SeedAdminAsync("admin", Password);
        await using (var other = fixture.CreateDbContext())
        {
            var tracked = await other.AdminUsers.FindAsync([admin.Id], Ct);
            tracked!.FailedAttempts = 3;
            tracked.LockoutUntil = clock.GetUtcNow().AddMinutes(5);
            await other.SaveChangesAsync(Ct);
        }

        var result = await service.UpdateAsync(admin.Id, new SaveAdminRequest("admin", "Mario Rossi", NewPassword), actor.Id, Ct);

        Assert.False(result.Value!.IsLocked);
        var stored = await fixture.FindAdminAsync(admin.Id);
        Assert.NotEqual(admin.SecurityStamp, stored!.SecurityStamp);
        Assert.NotEqual(admin.PasswordHash, stored.PasswordHash);
        Assert.Equal(0, stored.FailedAttempts);
        Assert.Null(stored.LockoutUntil);
    }

    [Fact]
    public async Task Update_WithWeakPassword_ReturnsValidation()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        var result = await service.UpdateAsync(admin.Id, new SaveAdminRequest("admin", "Mario Rossi", "weak"), ActorId, Ct);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Fact]
    public async Task Update_OwnPassword_Succeeds()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        var result = await service.UpdateAsync(admin.Id, new SaveAdminRequest("admin", "Mario Rossi", NewPassword), admin.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(admin.PasswordHash, (await fixture.FindAdminAsync(admin.Id))!.PasswordHash);
    }

    [Fact]
    public async Task Update_UsernameOnly_KeepsPassword()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        var result = await service.UpdateAsync(admin.Id, new SaveAdminRequest("renamed", "Mario Rossi", ""), ActorId, Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Update_UnknownAdmin_ReturnsNotFound()
    {
        var result = await service.UpdateAsync(42, new SaveAdminRequest("admin", "Mario Rossi", null), ActorId, Ct);

        Assert.Equal(AdminService.NotFoundCode, result.Error!.Code);
    }

    [Fact]
    public async Task Update_ToUsernameOfAnotherAdmin_ReturnsConflict()
    {
        await fixture.SeedAdminAsync("first", Password);
        var second = await fixture.SeedAdminAsync("second", Password);

        var result = await service.UpdateAsync(second.Id, new SaveAdminRequest("FIRST", "Mario Rossi", null), ActorId, Ct);

        Assert.Equal(AdminService.UsernameTaken, result.Error!.Code);
    }

    [Fact]
    public async Task Delete_Self_IsRejected()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        var result = await service.DeleteAsync(admin.Id, admin.Id, Ct);

        Assert.Equal(AdminService.CannotDeleteSelf, result.Error!.Code);
        Assert.NotNull(await fixture.FindAdminAsync(admin.Id));
    }

    [Fact]
    public async Task Delete_OtherAdmin_RemovesIt_ThenNotFound()
    {
        var admin = await fixture.SeedAdminAsync("admin", Password);

        Assert.True((await service.DeleteAsync(admin.Id, ActorId, Ct)).IsSuccess);
        Assert.Null(await fixture.FindAdminAsync(admin.Id));
        Assert.Equal(AdminService.NotFoundCode, (await service.DeleteAsync(admin.Id, ActorId, Ct)).Error!.Code);
    }
}
