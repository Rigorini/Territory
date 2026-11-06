using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;
using Territory.Application.Abstractions;
using Territory.Application.Domain;

namespace Territory.Infrastructure.Persistence;

/// <summary>
/// The schema is owned by the SQL scripts in /db; this mapping must stay aligned with them (no EF migrations).
/// Personal names are encrypted transparently by value converters, so they cannot be filtered, sorted or concatenated in SQL.
/// The model is cached per process together with its converters: every context must be built with the same protector.
/// </summary>
public sealed class TerritoryDbContext(DbContextOptions<TerritoryDbContext> options, IPersonalDataCipher protector)
    : DbContext(options), IDataProtectionKeyContext, ITerritoryDbContext
{
    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };

    // A failed commit whose outcome is unknown is simply run again: the operation replaces data and is idempotent.
    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
            async token =>
            {
                ChangeTracker.Clear();
                await operation(token);
            },
            _ => Task.FromResult(false),
            cancellationToken);

    public Task AcquireTransactionLockAsync(long key, CancellationToken cancellationToken) =>
        Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);

    public async Task<bool> RecordLoginFailureAsync(
        int userId, string clientKey, DateTimeOffset now, int maxAttempts, DateTimeOffset lockUntil, DateTimeOffset purgeBefore,
        CancellationToken cancellationToken)
    {
        await Database.ExecuteSqlAsync($"DELETE FROM login_lockout WHERE updated_at < {purgeBefore}", cancellationToken);

        // The first failure inserts the row, the next ones increment it; the lock is set by the failure that reaches the limit.
        var locked = await Database.SqlQuery<bool>($"""
            INSERT INTO login_lockout AS l (user_id, client_key, failed_attempts, lockout_until, updated_at)
            VALUES ({userId}, {clientKey}, 1, NULL, {now})
            ON CONFLICT (user_id, client_key) DO UPDATE SET
                failed_attempts = CASE WHEN l.failed_attempts + 1 >= {maxAttempts} THEN 0 ELSE l.failed_attempts + 1 END,
                lockout_until   = CASE WHEN l.failed_attempts + 1 >= {maxAttempts} THEN {lockUntil} ELSE l.lockout_until END,
                updated_at      = {now}
            RETURNING l.lockout_until IS NOT DISTINCT FROM {lockUntil} AS "Value"
            """).ToListAsync(cancellationToken);
        return locked.SingleOrDefault();
    }

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<LoginLockout> LoginLockouts => Set<LoginLockout>();
    public DbSet<Municipality> Municipalities => Set<Municipality>();
    public DbSet<TerritoryArea> Territories => Set<TerritoryArea>();
    public DbSet<Publisher> Publishers => Set<Publisher>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var personalData = new ValueConverter<string, string>(v => protector.Protect(v), v => protector.Unprotect(v));
        var optionalPersonalData = new ValueConverter<string?, string?>(
            v => v == null ? null : protector.Protect(v), v => v == null ? null : protector.Unprotect(v));

        modelBuilder.Entity<AdminUser>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.Username).HasMaxLength(AdminUser.UsernameMaxLength);
            e.Property(x => x.DisplayName).HasConversion(personalData);
            e.Property(x => x.Language).HasMaxLength(10);
            e.Property(x => x.Theme).HasConversion<short>();
            e.Property(x => x.Role).HasConversion<short>();
        });

        modelBuilder.Entity<LoginLockout>(e =>
        {
            e.ToTable("login_lockout");
            e.HasKey(x => new { x.UserId, x.ClientKey });
            e.Property(x => x.ClientKey).HasMaxLength(LoginLockout.ClientKeyMaxLength);
            e.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Municipality>(e =>
        {
            e.ToTable("municipality");
            e.Property(x => x.Name).HasMaxLength(Municipality.NameMaxLength);
        });

        modelBuilder.Entity<TerritoryArea>(e =>
        {
            e.ToTable("territory");
            e.Property(x => x.Name).HasMaxLength(TerritoryArea.NameMaxLength);
            e.Property(x => x.MyMapsId).HasColumnName("mymaps_id").HasMaxLength(TerritoryArea.MapIdMaxLength);
            e.HasIndex(x => new { x.MunicipalityId, x.Number }).IsUnique();
            e.HasOne(x => x.Municipality).WithMany(m => m.Territories)
                .HasForeignKey(x => x.MunicipalityId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Publisher>(e =>
        {
            e.ToTable("publisher");
            e.Property(x => x.FirstName).HasConversion(personalData);
            e.Property(x => x.LastName).HasConversion(personalData);
            e.Property(x => x.NameHash).HasMaxLength(Publisher.NameHashLength);
            e.HasIndex(x => x.NameHash).IsUnique();
        });

        modelBuilder.Entity<Assignment>(e =>
        {
            e.ToTable("assignment");
            e.Ignore(x => x.IsOpen);
            e.Property(x => x.ReturnedBy).HasConversion<short>();
            e.Property(x => x.ReturnedByName).HasConversion(optionalPersonalData);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.Territory).WithMany(t => t.Assignments)
                .HasForeignKey(x => x.TerritoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Publisher).WithMany()
                .HasForeignKey(x => x.PublisherId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }
}
