using Microsoft.EntityFrameworkCore;
using Territory.Application.Domain;

namespace Territory.Application.Abstractions;

public interface ITerritoryDbContext
{
    DbSet<AdminUser> AdminUsers { get; }
    DbSet<Municipality> Municipalities { get; }
    DbSet<TerritoryArea> Territories { get; }
    DbSet<Publisher> Publishers { get; }
    DbSet<Assignment> Assignments { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the operation in one transaction (retried as a whole on transient failures, so it must not keep state
    /// between attempts); everything is rolled back when it throws.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);

    /// <summary>Serializes concurrent transactions on the given key until the current transaction ends. Must run inside <see cref="ExecuteInTransactionAsync"/>.</summary>
    Task AcquireTransactionLockAsync(long key, CancellationToken cancellationToken);

    /// <summary>True when the exception is caused by a unique constraint/index violation.</summary>
    bool IsUniqueViolation(DbUpdateException exception);

    /// <summary>True when the exception is caused by a foreign key violation (referenced row missing or deleted).</summary>
    bool IsForeignKeyViolation(DbUpdateException exception);
}
