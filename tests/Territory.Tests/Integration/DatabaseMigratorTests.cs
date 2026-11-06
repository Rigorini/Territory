using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Territory.Api.Cli;
using Territory.Infrastructure.Persistence;

namespace Territory.Tests.Integration;

/// <summary>Each test works on its own empty database inside the shared container.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class DatabaseMigratorTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int LatestVersion => DatabaseMigrator.Scripts[^1].Version;

    [Fact]
    public void EmbeddedScripts_AreNumberedFromOneWithoutGaps()
    {
        var versions = DatabaseMigrator.Scripts.Select(s => s.Version).ToList();

        Assert.Equal(Enumerable.Range(1, versions.Count), versions);
        Assert.Equal("001_schema.sql", DatabaseMigrator.Scripts[0].Name);
    }

    [Fact]
    public void EmbeddedScripts_DoNotManageTheirOwnTransaction() =>
        Assert.All(DatabaseMigrator.Scripts, s =>
            Assert.DoesNotMatch(@"(?im)^\s*(BEGIN|COMMIT|ROLLBACK)\s*;", s.Sql));

    [Fact]
    public void Checksum_IgnoresLineEndings()
    {
        Assert.Equal(new SchemaScript(1, "001_a.sql", "a\nb").Checksum, new SchemaScript(1, "001_a.sql", "a\r\nb").Checksum);
        Assert.NotEqual(new SchemaScript(1, "001_a.sql", "a\nb").Checksum, new SchemaScript(1, "001_a.sql", "a\nc").Checksum);
    }

    [Fact]
    public async Task Migrate_EmptyDatabase_AppliesEveryScriptAndRecordsIt()
    {
        var database = await fixture.CreateEmptyDatabaseAsync();

        var result = await PostgresFixture.CreateMigrator(database).MigrateAsync(Ct);

        Assert.Equal(DatabaseMigrator.Scripts, result.Applied);
        Assert.Equal(LatestVersion, result.CurrentVersion);
        Assert.Equal(DatabaseMigrator.Scripts.Select(s => (s.Version, s.Checksum)), await ReadVersionsAsync(database));
        Assert.True(await TableExistsAsync(database, "login_lockout"));
    }

    [Fact]
    public async Task Migrate_UpToDateDatabase_DoesNothing()
    {
        var database = await fixture.CreateEmptyDatabaseAsync();
        await PostgresFixture.CreateMigrator(database).MigrateAsync(Ct);

        var result = await PostgresFixture.CreateMigrator(database).MigrateAsync(Ct);

        Assert.Empty(result.Applied);
        Assert.Equal(LatestVersion, result.CurrentVersion);
    }

    [Fact]
    public async Task Migrate_DatabaseCreatedBeforeVersioning_KeepsDataAndAddsWhatIsMissing()
    {
        // As in production up to 1.4.4: only the baseline script, run by hand, and no version table.
        var database = await fixture.CreateEmptyDatabaseAsync();
        await PostgresFixture.ExecuteAsync(database, DatabaseMigrator.Scripts[0].Sql);
        await PostgresFixture.ExecuteAsync(database,
            "INSERT INTO users (username, display_name, password_hash) VALUES ('admin', 'enc:v1:x', 'hash')");

        var result = await PostgresFixture.CreateMigrator(database).MigrateAsync(Ct);

        Assert.Equal(DatabaseMigrator.Scripts, result.Applied);
        Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM users WHERE username = 'admin'"));
        Assert.True(await TableExistsAsync(database, "login_lockout"));
    }

    [Fact]
    public async Task Migrate_FromSeveralReplicasAtOnce_AppliesEachScriptOnce()
    {
        var database = await fixture.CreateEmptyDatabaseAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => PostgresFixture.CreateMigrator(database).MigrateAsync(Ct)));

        Assert.Equal(DatabaseMigrator.Scripts.Count, results.Sum(r => r.Applied.Count));
        Assert.All(results, r => Assert.Equal(LatestVersion, r.CurrentVersion));
    }

    [Fact]
    public async Task Migrate_FailingScript_RollsItBackAndStopsTheStartup()
    {
        var database = await fixture.CreateEmptyDatabaseAsync();
        var logs = new CapturingLoggerFactory();
        SchemaScript[] scripts =
        [
            new(1, "001_ok.sql", "CREATE TABLE ok_table (id int)"),
            new(2, "002_broken.sql", "CREATE TABLE half_done (id int); SELECT * FROM missing_table")
        ];

        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresFixture.CreateMigrator(database, logs, scripts).MigrateAsync(Ct));

        Assert.Equal([(1, scripts[0].Checksum)], await ReadVersionsAsync(database));
        Assert.True(await TableExistsAsync(database, "ok_table"));
        Assert.False(await TableExistsAsync(database, "half_done"));
        Assert.Equal(LogLevel.Critical, Assert.Single(logs.ForEvent(3003)).Level);

        // Fixed in the next release: the script runs again from the start.
        scripts[1] = new SchemaScript(2, "002_broken.sql", "CREATE TABLE half_done (id int)");
        var retry = await PostgresFixture.CreateMigrator(database, scripts: scripts).MigrateAsync(Ct);
        Assert.Equal([scripts[1]], retry.Applied);
    }

    [Fact]
    public async Task Migrate_ChangedOrUnknownAppliedScript_IsReportedButNotRerun()
    {
        var database = await fixture.CreateEmptyDatabaseAsync();
        await PostgresFixture.CreateMigrator(database, scripts: [new(1, "001_a.sql", "CREATE TABLE a (id int)"), new(2, "002_b.sql", "CREATE TABLE b (id int)")])
            .MigrateAsync(Ct);
        var logs = new CapturingLoggerFactory();

        // An older build (no script 2) whose script 1 was edited after being applied.
        var result = await PostgresFixture.CreateMigrator(database, logs, [new(1, "001_a.sql", "CREATE TABLE a (id int, x int)")])
            .MigrateAsync(Ct);

        Assert.Empty(result.Applied);
        Assert.Equal(2, result.CurrentVersion);
        Assert.Contains("001_a.sql", Assert.Single(logs.ForEvent(3004)).Message, StringComparison.Ordinal);
        Assert.Contains("2", Assert.Single(logs.ForEvent(3005)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MigrateCommand_PrefersTheMigrationConnection()
    {
        var database = await fixture.CreateEmptyDatabaseAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Territory"] = "Host=invalid.invalid",
            ["ConnectionStrings:TerritoryMigrations"] = database
        }).Build();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DatabaseCommands.RunAsync(configuration, output, error, Ct);

        Assert.Equal((0, string.Empty), (exitCode, error.ToString()));
        Assert.Contains($"versione {LatestVersion}", output.ToString(), StringComparison.Ordinal);
        Assert.True(await TableExistsAsync(database, "login_lockout"));
    }

    private static async Task<List<(int, string)>> ReadVersionsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand($"SELECT version, checksum FROM {DatabaseMigrator.VersionTable} ORDER BY version", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<(int, string)>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string table) =>
        await ScalarAsync<bool>(connectionString, $"SELECT to_regclass('public.{table}') IS NOT NULL");

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }
}
