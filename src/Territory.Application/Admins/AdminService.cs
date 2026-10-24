using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Territory.Application.Abstractions;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Security;

namespace Territory.Application.Admins;

public sealed record AdminDto(int Id, string Username, string DisplayName, DateTimeOffset CreatedAt, bool IsLocked, AdminRole Role);

/// <summary>
/// DisplayName is the name of the person, Username the login. Password is required on create; on update an empty password keeps the current one.
/// Role defaults to Administrator on create and keeps the current one on update when null.
/// A password set by an administrator (on create, or on another user's account) is temporary:
/// the owner must replace it at the next login.
/// </summary>
public sealed record SaveAdminRequest(string? Username, string? DisplayName, string? Password, AdminRole? Role = null);

public sealed class AdminService(
    ITerritoryDbContext db,
    IPasswordHasher<AdminUser> hasher,
    TimeProvider clock,
    SecurityAuditLogger audit)
{
    public const string UsernameTaken = "admins.usernameTaken";
    public const string NotFoundCode = "admins.notFound";
    public const string CannotDeleteSelf = "admins.cannotDeleteSelf";
    public const string CannotChangeOwnRole = "admins.cannotChangeOwnRole";

    public async Task<IReadOnlyList<AdminDto>> ListAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var admins = await db.AdminUsers
            .AsNoTracking()
            .Select(u => new AdminDto(u.Id, u.Username, u.DisplayName, u.CreatedAt, u.LockoutUntil != null && u.LockoutUntil > now, u.Role))
            .ToListAsync(cancellationToken);
        return admins
            .OrderBy(a => a.DisplayName, NameOrdering.Comparer)
            .ThenBy(a => a.Username, NameOrdering.Comparer)
            .ToList();
    }

    public async Task<Result<AdminDto>> CreateAsync(
        SaveAdminRequest request, int actorId, CancellationToken cancellationToken, bool mustChangePassword = true)
    {
        var username = request.Username?.Trim();
        var errors = new ValidationErrors();
        AdminRules.ValidateUsername(errors, username);
        var displayName = AdminRules.ValidateDisplayName(errors, request.DisplayName);
        AdminRules.ValidatePassword(errors, request.Password);
        AdminRules.ValidateRole(errors, request.Role);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        if (await UsernameExistsAsync(username!, excludeId: null, cancellationToken))
        {
            return AppError.Conflict(UsernameTaken);
        }

        var user = new AdminUser
        {
            Username = username!,
            DisplayName = displayName!,
            Role = request.Role ?? AdminRole.Administrator,
            MustChangePassword = mustChangePassword,
            PasswordHash = string.Empty,
            CreatedAt = clock.GetUtcNow()
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password!);
        db.AdminUsers.Add(user);

        if (await db.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AppError.Conflict(UsernameTaken);
        }

        audit.AdminCreated(user.Id, actorId);
        return ToDto(user);
    }

    public async Task<Result<AdminDto>> UpdateAsync(int id, SaveAdminRequest request, int actorId, CancellationToken cancellationToken)
    {
        var changePassword = !string.IsNullOrEmpty(request.Password);
        var username = request.Username?.Trim();
        var errors = ValidateUpdate(username, request, changePassword);
        var displayName = AdminRules.ValidateDisplayName(errors, request.DisplayName);
        AdminRules.ValidateRole(errors, request.Role);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var user = await db.AdminUsers.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return AppError.NotFound(NotFoundCode);
        }

        if (await UsernameExistsAsync(username!, id, cancellationToken))
        {
            return AppError.Conflict(UsernameTaken);
        }

        var roleChanged = request.Role is { } role && role != user.Role;
        if (roleChanged && id == actorId)
        {
            return AppError.BusinessRule(CannotChangeOwnRole);
        }

        user.Username = username!;
        user.DisplayName = displayName!;
        user.Role = request.Role ?? user.Role;
        if (changePassword)
        {
            SetPassword(user, request.Password!);
            // A password chosen by an administrator is temporary: whoever receives it must
            // replace it at the next login. Changing your own password here is not temporary.
            if (id != actorId)
            {
                user.MustChangePassword = true;
            }
        }

        if (await db.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AppError.Conflict(UsernameTaken);
        }

        audit.AdminUpdated(user.Id, actorId, changePassword);
        if (roleChanged)
        {
            audit.AdminRoleChanged(user.Id, user.Role, actorId);
        }

        return ToDto(user);
    }

    public async Task<Result<Unit>> DeleteAsync(int id, int actorId, CancellationToken cancellationToken)
    {
        if (id == actorId)
        {
            return AppError.BusinessRule(CannotDeleteSelf);
        }

        var deleted = await db.AdminUsers.Where(u => u.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            return AppError.NotFound(NotFoundCode);
        }

        audit.AdminDeleted(id, actorId);
        return Unit.Value;
    }

    private static ValidationErrors ValidateUpdate(string? username, SaveAdminRequest request, bool changePassword)
    {
        var errors = new ValidationErrors();
        AdminRules.ValidateUsername(errors, username);
        if (changePassword)
        {
            AdminRules.ValidatePassword(errors, request.Password);
        }

        return errors;
    }

    private void SetPassword(AdminUser user, string password)
    {
        user.PasswordHash = hasher.HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid();
        user.FailedAttempts = 0;
        user.LockoutUntil = null;
    }

    private Task<bool> UsernameExistsAsync(string username, int? excludeId, CancellationToken cancellationToken)
    {
        var normalized = TextRules.Normalize(username);
        return db.AdminUsers.AnyAsync(
            u => u.Username.ToLower() == normalized && (excludeId == null || u.Id != excludeId),
            cancellationToken);
    }

    private AdminDto ToDto(AdminUser user) =>
        new(user.Id, user.Username, user.DisplayName, user.CreatedAt, user.LockoutUntil > clock.GetUtcNow(), user.Role);
}
