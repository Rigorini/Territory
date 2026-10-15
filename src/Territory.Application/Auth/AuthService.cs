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
    public const string InvalidCredentials = "auth.invalidCredentials";
    public const string SessionExpired = "auth.sessionExpired";
    public const string CurrentPasswordField = "currentPassword";
    public const string WrongPassword = "wrongPassword";
    public const string ThemeField = "theme";
    public const string LanguageField = "language";

    // Verified when the username does not exist so both paths take comparable time (no user enumeration).
    private static readonly string TimingHash =
        new PasswordHasher<AdminUser>().HashPassword(null!, Guid.NewGuid().ToString());

    public async Task<Result<AdminUser>> LoginAsync(string? username, string? password, CancellationToken cancellationToken)
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
        if (user.LockoutUntil > now)
        {
            audit.LoginFailed(SecurityAuditLogger.ReasonLocked, user.Id);
            return AppError.Unauthorized(InvalidCredentials);
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password!);
        if (verification == PasswordVerificationResult.Failed)
        {
            audit.LoginFailed(SecurityAuditLogger.ReasonWrongPassword, user.Id);
            await RegisterFailureAsync(user, now, cancellationToken);
            return AppError.Unauthorized(InvalidCredentials);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password!);
        }

        user.FailedAttempts = 0;
        user.LockoutUntil = null;
        await db.SaveChangesAsync(cancellationToken);
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
    public async Task<Result<AdminUser>> ChangePasswordAsync(int adminId, ChangePasswordRequest request, CancellationToken cancellationToken)
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
        if (user.LockoutUntil > now)
        {
            audit.PasswordChangeFailed(SecurityAuditLogger.ReasonLocked, adminId);
            return WrongPasswordError();
        }

        var current = request.CurrentPassword!;
        if (current.Length > PasswordPolicy.MaxLength
            || hasher.VerifyHashedPassword(user, user.PasswordHash, current) == PasswordVerificationResult.Failed)
        {
            audit.PasswordChangeFailed(SecurityAuditLogger.ReasonWrongPassword, adminId);
            await RegisterFailureAsync(user, now, cancellationToken);
            return WrongPasswordError();
        }

        user.PasswordHash = hasher.HashPassword(user, request.Password!);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid();
        user.FailedAttempts = 0;
        user.LockoutUntil = null;
        await db.SaveChangesAsync(cancellationToken);
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

    // Single atomic UPDATE: safe when several replicas process failed logins for the same account.
    private async Task RegisterFailureAsync(AdminUser user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var adminId = user.Id;
        DateTimeOffset? lockUntil = now.Add(LockoutDuration);
        await db.AdminUsers
            .Where(u => u.Id == adminId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LockoutUntil, u => u.FailedAttempts + 1 >= MaxFailedAttempts ? lockUntil : u.LockoutUntil)
                .SetProperty(u => u.FailedAttempts, u => u.FailedAttempts + 1 >= MaxFailedAttempts ? 0 : u.FailedAttempts + 1),
                cancellationToken);

        // Based on the counter read before the update: under concurrent failures the audit entry may be off by one attempt.
        if (user.FailedAttempts + 1 >= MaxFailedAttempts)
        {
            audit.AccountLocked(adminId, lockUntil.Value);
        }
    }
}
