using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Territory.Application.Abstractions;
using Territory.Application.Admins;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Security;

namespace Territory.Application.Auth;

public sealed record AdminProfile(int Id, string Username, string DisplayName, ThemePreference Theme, string? Language, AdminRole Role, bool MustChangePassword = false);

/// <summary>What a valid session needs to know about its account; the database is the source of truth.</summary>
public sealed record SessionState(AdminRole Role, bool MustChangePassword);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? Password);

/// <summary>Language null means "follow the browser".</summary>
public sealed record PreferencesRequest(ThemePreference? Theme, string? Language);

public sealed class AuthService(
    ITerritoryDbContext db,
    IPasswordHasher<AdminUser> hasher,
    TimeProvider clock,
    SecurityAuditLogger audit)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    /// <summary>Failures older than this are forgotten: isolated mistakes do not add up forever.</summary>
    public static readonly TimeSpan FailureMemory = TimeSpan.FromDays(1);
    public const string InvalidCredentials = "auth.invalidCredentials";
    public const string SessionExpired = "auth.sessionExpired";
    public const string CurrentPasswordField = "currentPassword";
    public const string WrongPassword = "wrongPassword";
    public const string ThemeField = "theme";
    public const string LanguageField = "language";

    // Verified when the username does not exist so both paths take comparable time (no user enumeration).
    private static readonly string TimingHash =
        new PasswordHasher<AdminUser>().HashPassword(null!, Guid.NewGuid().ToString());

    /// <param name="clientKey">Network of the caller (<see cref="ClientNetwork.KeyOf"/>): failures and lockouts are counted per account and network.</param>
    public async Task<Result<AdminUser>> LoginAsync(string? username, string? password, string clientKey, CancellationToken cancellationToken)
    {
        if (!IsPlausible(username, password))
        {
            return AppError.Unauthorized(InvalidCredentials);
        }

        var normalized = username!.Trim().ToLowerInvariant();
        var user = await db.AdminUsers.SingleOrDefaultAsync(u => u.Username.ToLower() == normalized, cancellationToken);
        if (user is null)
        {
            hasher.VerifyHashedPassword(null!, TimingHash, password!);
            audit.LoginFailed(SecurityAuditLogger.ReasonUnknownUser, null);
            return AppError.Unauthorized(InvalidCredentials);
        }

        var now = clock.GetUtcNow();
        if (await IsLockedAsync(user.Id, clientKey, now, cancellationToken))
        {
            audit.LoginFailed(SecurityAuditLogger.ReasonLocked, user.Id);
            return AppError.Unauthorized(InvalidCredentials);
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password!);
        if (verification == PasswordVerificationResult.Failed)
        {
            audit.LoginFailed(SecurityAuditLogger.ReasonWrongPassword, user.Id);
            await RegisterFailureAsync(user.Id, clientKey, now, cancellationToken);
            return AppError.Unauthorized(InvalidCredentials);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password!);
        }

        await db.SaveChangesAsync(cancellationToken);
        await ClearFailuresAsync(user.Id, clientKey, cancellationToken);
        audit.LoginSucceeded(user.Id);
        return user;
    }

    /// <returns>The current state of the account when the session is still valid, otherwise null.</returns>
    public Task<SessionState?> GetSessionAsync(int adminId, Guid securityStamp, CancellationToken cancellationToken) =>
        db.AdminUsers
            .AsNoTracking()
            .Where(u => u.Id == adminId && u.SecurityStamp == securityStamp)
            .Select(u => new SessionState(u.Role, u.MustChangePassword))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<AdminProfile?> GetProfileAsync(int adminId, CancellationToken cancellationToken) =>
        db.AdminUsers
            .AsNoTracking()
            .Where(u => u.Id == adminId)
            .Select(u => new AdminProfile(u.Id, u.Username, u.DisplayName, u.Theme, u.Language, u.Role, u.MustChangePassword))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<Result<AdminProfile>> UpdatePreferencesAsync(int adminId, PreferencesRequest request, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        errors.AddIf(request.Theme is null, ThemeField, TextRules.Required);
        errors.AddIf(request.Theme is { } theme && !Enum.IsDefined(theme), ThemeField, TextRules.InvalidFormat);
        errors.AddIf(request.Language is not null && !Languages.IsSupported(request.Language), LanguageField, TextRules.InvalidFormat);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var updated = await db.AdminUsers
            .Where(u => u.Id == adminId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Theme, request.Theme!.Value)
                .SetProperty(u => u.Language, request.Language), cancellationToken);
        if (updated == 0)
        {
            return AppError.Unauthorized(SessionExpired);
        }

        return (await GetProfileAsync(adminId, cancellationToken))!;
    }

    /// <summary>
    /// Lets a user change their own password after proving they know the current one. Wrong attempts count
    /// towards the login lockout, so a stolen session cannot be used to brute-force the password.
    /// The security stamp rotates: every other session of the user is invalidated.
    /// </summary>
    public async Task<Result<AdminUser>> ChangePasswordAsync(
        int adminId, ChangePasswordRequest request, string clientKey, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        errors.AddIf(string.IsNullOrEmpty(request.CurrentPassword), CurrentPasswordField, TextRules.Required);
        AdminRules.ValidatePassword(errors, request.Password);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var user = await db.AdminUsers.SingleOrDefaultAsync(u => u.Id == adminId, cancellationToken);
        if (user is null)
        {
            return AppError.Unauthorized(SessionExpired);
        }

        var now = clock.GetUtcNow();
        if (await IsLockedAsync(adminId, clientKey, now, cancellationToken))
        {
            audit.PasswordChangeFailed(SecurityAuditLogger.ReasonLocked, adminId);
            return WrongPasswordError();
        }

        var current = request.CurrentPassword!;
        if (current.Length > PasswordPolicy.MaxLength
            || hasher.VerifyHashedPassword(user, user.PasswordHash, current) == PasswordVerificationResult.Failed)
        {
            audit.PasswordChangeFailed(SecurityAuditLogger.ReasonWrongPassword, adminId);
            await RegisterFailureAsync(adminId, clientKey, now, cancellationToken);
            return WrongPasswordError();
        }

        user.PasswordHash = hasher.HashPassword(user, request.Password!);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid();
        await db.SaveChangesAsync(cancellationToken);
        // New password: failures counted against the old one, from any network, no longer matter.
        await db.LoginLockouts.Where(l => l.UserId == adminId).ExecuteDeleteAsync(cancellationToken);
        audit.PasswordChanged(adminId);
        return user;
    }

    private static AppError WrongPasswordError() =>
        new ValidationErrors().Add(CurrentPasswordField, WrongPassword).ToError();
    private static bool IsPlausible(string? username, string? password) =>
        !string.IsNullOrWhiteSpace(username)
        && username.Length <= AdminUser.UsernameMaxLength
        && !string.IsNullOrEmpty(password)
        && password.Length <= PasswordPolicy.MaxLength;

    private Task<bool> IsLockedAsync(int adminId, string clientKey, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.LoginLockouts.AnyAsync(
            l => l.UserId == adminId && l.ClientKey == clientKey && l.LockoutUntil > now, cancellationToken);

    private Task ClearFailuresAsync(int adminId, string clientKey, CancellationToken cancellationToken) =>
        db.LoginLockouts.Where(l => l.UserId == adminId && l.ClientKey == clientKey).ExecuteDeleteAsync(cancellationToken);

    private async Task RegisterFailureAsync(int adminId, string clientKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var lockUntil = now.Add(LockoutDuration);
        if (await db.RecordLoginFailureAsync(
                adminId, clientKey, now, MaxFailedAttempts, lockUntil, now.Subtract(FailureMemory), cancellationToken))
        {
            audit.AccountLocked(adminId, lockUntil);
        }
    }
}
