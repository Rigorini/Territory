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
    DbSet<LoginLockout> LoginLockouts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts one failed attempt of the user from the client in a single atomic statement (safe across replicas).
    /// On the <paramref name="maxAttempts"/>-th failure (at least 2) the pair is locked until <paramref name="lockUntil"/> and the counter restarts.
    /// Rows not updated since <paramref name="purgeBefore"/> are deleted first.
    /// </summary>
    /// <returns>True when this failure locked the pair.</returns>
    Task<bool> RecordLoginFailureAsync(
        int userId, string clientKey, DateTimeOffset now, int maxAttempts, DateTimeOffset lockUntil, DateTimeOffset purgeBefore,
        CancellationToken cancellationToken);

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
