using Microsoft.EntityFrameworkCore;
using Territory.Application.Abstractions;
using Territory.Application.Admins;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Security;

namespace Territory.Application.Setup;

/// <summary>First-access setup: while no administrator exists the first one can be created without authentication.</summary>
public sealed class SetupService(ITerritoryDbContext db, AdminService admins, SecurityAuditLogger audit)
{
    public const string AlreadyDone = "setup.alreadyDone";

    // Arbitrary constant shared by every replica so that two simultaneous first accesses cannot both succeed.
    private const long SetupLockKey = 0x5445_5252_5345_5455;

    public async Task<bool> IsRequiredAsync(CancellationToken cancellationToken) =>
        !await db.AdminUsers.AnyAsync(cancellationToken);

    public async Task<Result<AdminDto>> CreateFirstAdminAsync(SaveAdminRequest request, CancellationToken cancellationToken)
    {
        Result<AdminDto> result = AppError.Conflict(AlreadyDone);
        await db.ExecuteInTransactionAsync(async token =>
        {
            await db.AcquireTransactionLockAsync(SetupLockKey, token);
            result = await db.AdminUsers.AnyAsync(token)
                ? AppError.Conflict(AlreadyDone)
                : await admins.CreateAsync(request with { Role = AdminRole.Administrator }, actorId: 0, token, mustChangePassword: false);
        }, cancellationToken);

        if (result.IsSuccess)
        {
            audit.FirstAdminCreated(result.Value!.Id);
        }

        return result;
    }
}
