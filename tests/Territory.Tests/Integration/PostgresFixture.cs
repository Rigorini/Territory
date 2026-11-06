using Territory.Application.Assignments;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Territory.Application.Domain;
using Territory.Application.Publishers;
using Territory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Territory.Tests.Integration;

/// <summary>One disposable PostgreSQL container per test collection, initialised with the real db scripts.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();
        await CreateMigrator(ConnectionString).MigrateAsync(CancellationToken.None);
    }

    /// <summary>The migrator of the application, as run at startup.</summary>
    public static DatabaseMigrator CreateMigrator(
        string connectionString, ILoggerFactory? logs = null, IReadOnlyList<SchemaScript>? scripts = null) =>
        new(connectionString, (logs ?? NullLoggerFactory.Instance).CreateLogger<DatabaseMigrator>(), TimeProvider.System, scripts);

    /// <summary>Creates an empty database in the same container and returns its connection string.</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = "db_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync() => container.DisposeAsync();

    public TerritoryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TerritoryDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options,
            TestPersonalData.Protector);

    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE assignment, territory, municipality, publisher, users RESTART IDENTITY CASCADE");
    }

    public async Task<AdminUser> SeedAdminAsync(string username, string password, AdminRole role = AdminRole.Administrator)
    {
        await using var db = CreateDbContext();
        var user = new AdminUser { Username = username, DisplayName = "Admin " + username, Role = role, PasswordHash = string.Empty, CreatedAt = DateTimeOffset.UtcNow };
        user.PasswordHash = new PasswordHasher<AdminUser>().HashPassword(user, password);
        db.AdminUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<AdminUser?> FindAdminAsync(int id)
    {
        await using var db = CreateDbContext();
        return await db.AdminUsers.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
    }

    public async Task<LoginLockout?> FindLockoutAsync(int userId, string clientKey)
    {
        await using var db = CreateDbContext();
        return await db.LoginLockouts.AsNoTracking().SingleOrDefaultAsync(l => l.UserId == userId && l.ClientKey == clientKey);
    }

    public async Task<int> CountLockoutsAsync(int userId)
    {
        await using var db = CreateDbContext();
        return await db.LoginLockouts.CountAsync(l => l.UserId == userId);
    }

    public Task<LoginLockout> SeedLockoutAsync(int userId, string clientKey, int failedAttempts, DateTimeOffset? lockoutUntil, DateTimeOffset updatedAt) =>
        AddAsync(new LoginLockout
        {
            UserId = userId,
            ClientKey = clientKey,
            FailedAttempts = failedAttempts,
            LockoutUntil = lockoutUntil,
            UpdatedAt = updatedAt
        });

    public Task<Municipality> SeedMunicipalityAsync(string name) => AddAsync(new Municipality { Name = name });

    public Task<TerritoryArea> SeedTerritoryAsync(int municipalityId, int number, string mapId = "map-id-0000000001") =>
        AddAsync(new TerritoryArea { MunicipalityId = municipalityId, Number = number, Name = $"Territory {number}", MyMapsId = mapId });

    public Task<Publisher> SeedPublisherAsync(string firstName, string lastName, bool deleted = false) =>
        AddAsync(new Publisher
        {
            FirstName = firstName,
            LastName = lastName,
            NameHash = TestPersonalData.Protector.BlindIndex(PublisherService.NameIdentity(firstName, lastName)),
            IsDeleted = deleted,
            DeletedAt = deleted ? DateTimeOffset.UtcNow : null
        });

    public Task<Assignment> SeedAssignmentAsync(int territoryId, int publisherId, bool open)
    {
        var assignedAt = DateTimeOffset.UtcNow.AddDays(-30);
        return AddAsync(new Assignment
        {
            TerritoryId = territoryId,
            PublisherId = publisherId,
            AssignedAt = assignedAt,
            ReturnedAt = open ? null : assignedAt.AddDays(10),
            ReturnedBy = open ? null : ReturnedBy.Admin,
            TokenHash = open ? Guid.NewGuid().ToByteArray() : null
        });
    }

/// <summary>Open assignment whose clear share token is known to the test.</summary>
    public async Task<(Assignment Assignment, string Token)> SeedSharedAssignmentAsync(int territoryId, int publisherId)
    {
        var issued = ShareToken.Issue();
        var assignment = await AddAsync(new Assignment
        {
            TerritoryId = territoryId,
            PublisherId = publisherId,
            AssignedAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()),
            TokenHash = issued.Hash
        });
        return (assignment, issued.Token);
    }

    public Task<Assignment> SeedHistoryAsync(
        int territoryId, int publisherId, DateTimeOffset assignedAt, DateTimeOffset? returnedAt, ReturnedBy? returnedBy = null) =>
        AddAsync(new Assignment
        {
            TerritoryId = territoryId,
            PublisherId = publisherId,
            AssignedAt = assignedAt,
            ReturnedAt = returnedAt,
            ReturnedBy = returnedAt is null ? null : returnedBy ?? ReturnedBy.Admin,
            TokenHash = returnedAt is null ? Guid.NewGuid().ToByteArray() : null
        });

    public async Task SetMapCacheAsync(int territoryId, string? kml, DateTimeOffset? fetchedAt)
    {
        await using var db = CreateDbContext();
        await db.Territories
            .Where(t => t.Id == territoryId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MapKml, kml).SetProperty(t => t.MapFetchedAt, fetchedAt));
    }

    public async Task<T?> FindAsync<T>(params object[] keys) where T : class
    {
        await using var db = CreateDbContext();
        return await db.Set<T>().FindAsync(keys);
    }

    private async Task<T> AddAsync<T>(T entity) where T : class
    {
        await using var db = CreateDbContext();
        db.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "database";
}
