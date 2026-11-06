using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Territory.Infrastructure.Persistence;

/// <summary>A versioned schema script (db/NNN_name.sql) shipped inside this assembly.</summary>
public sealed record SchemaScript(int Version, string Name, string Sql)
{
    /// <summary>SHA-256 of the script with normalised line endings, so Windows and Linux checkouts agree.</summary>
    public string Checksum { get; } =
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Sql.ReplaceLineEndings("\n"))));
}

/// <summary>Outcome of a migration run.</summary>
public sealed record MigrationResult(IReadOnlyList<SchemaScript> Applied, int CurrentVersion);

/// <summary>
/// Brings the database schema up to date at startup. Scripts are applied in version order, each one in its own
/// transaction together with its row in <c>schema_version</c>, so a failed script leaves no trace and is retried
/// at the next start. A session advisory lock lets only one replica migrate at a time; the others wait and then
/// find nothing to do. Databases created before versioning have no <c>schema_version</c>: they simply run the
/// idempotent baseline (version 1) again.
/// </summary>
/// <param name="scripts">Scripts to apply; defaults to the ones embedded in this build (<see cref="Scripts"/>).</param>
public sealed partial class DatabaseMigrator(
    string connectionString, ILogger<DatabaseMigrator> logger, TimeProvider clock, IReadOnlyList<SchemaScript>? scripts = null)
{
    public const string VersionTable = "schema_version";
    private const string ResourcePrefix = "Territory.Db.";

    // Arbitrary constant shared by every replica (and different from the setup lock).
    private const long MigrationLockKey = 0x5445_5252_4D49_4752;
    private const int CommandTimeoutSeconds = 300;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>Every script of this build, in version order.</summary>
    public static IReadOnlyList<SchemaScript> Scripts { get; } = LoadScripts();

    private readonly IReadOnlyList<SchemaScript> scripts = scripts ?? Scripts;

    /// <summary>Retries transient failures: on shared hosting the database is often still waking up when the app starts.</summary>
    public async Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await MigrateOnceAsync(cancellationToken);
            }
            catch (NpgsqlException ex) when (ex.IsTransient && attempt < MaxAttempts)
            {
                var delay = FirstRetryDelay * Math.Pow(2, attempt - 1);
                LogRetrying(ex, attempt, delay);
                await Task.Delay(delay, clock, cancellationToken);
            }
        }
    }

    private async Task<MigrationResult> MigrateOnceAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, null, $"SELECT pg_advisory_lock({MigrationLockKey})", cancellationToken);
        try
        {
            await ExecuteAsync(connection, null, $"""
                CREATE TABLE IF NOT EXISTS {VersionTable} (
                    version    integer      PRIMARY KEY,
                    name       varchar(200) NOT NULL,
                    checksum   char(64)     NOT NULL,
                    applied_at timestamptz  NOT NULL DEFAULT now()
                )
                """, cancellationToken);

            var applied = await ReadAppliedAsync(connection, cancellationToken);
            WarnAboutDrift(applied);

            var done = new List<SchemaScript>();
            foreach (var script in scripts.OrderBy(s => s.Version).Where(s => !applied.ContainsKey(s.Version)))
            {
                await ApplyAsync(connection, script, cancellationToken);
                done.Add(script);
            }

            var current = applied.Keys.Concat(done.Select(s => s.Version)).DefaultIfEmpty(0).Max();
            LogUpToDate(current, done.Count);
            return new MigrationResult(done, current);
        }
        finally
        {
            await UnlockAsync(connection);
        }
    }

    // Released explicitly: with pooling, closing the connection would not end the session holding the lock.
    // When the connection itself is broken the session is gone and so is the lock: the original error matters more.
    private static async Task UnlockAsync(NpgsqlConnection connection)
    {
        try
        {
            await ExecuteAsync(connection, null, $"SELECT pg_advisory_unlock({MigrationLockKey})", CancellationToken.None);
        }
        catch (NpgsqlException)
        {
            NpgsqlConnection.ClearPool(connection);
        }
    }

    private async Task ApplyAsync(NpgsqlConnection connection, SchemaScript script, CancellationToken cancellationToken)
    {
        LogApplying(script.Version, script.Name);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ExecuteAsync(connection, transaction, script.Sql, cancellationToken);
            await using var record = new NpgsqlCommand(
                $"INSERT INTO {VersionTable} (version, name, checksum) VALUES (@version, @name, @checksum)", connection, transaction);
            record.Parameters.AddWithValue("version", script.Version);
            record.Parameters.AddWithValue("name", script.Name);
            record.Parameters.AddWithValue("checksum", script.Checksum);
            await record.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex, script.Version, script.Name);
            throw;
        }
    }

    private void WarnAboutDrift(IReadOnlyDictionary<int, string> applied)
    {
        var known = scripts.ToDictionary(s => s.Version);
        foreach (var (version, checksum) in applied)
        {
            if (!known.TryGetValue(version, out var script))
            {
                // Typically an older build started on a database already migrated by a newer one.
                LogUnknownVersion(version);
            }
            else if (!string.Equals(script.Checksum, checksum.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                LogChecksumMismatch(version, script.Name);
            }
        }
    }

    private static async Task<Dictionary<int, string>> ReadAppliedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT version, checksum FROM {VersionTable}", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var applied = new Dictionary<int, string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            applied[reader.GetInt32(0)] = reader.GetString(1);
        }

        return applied;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = CommandTimeoutSeconds };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static List<SchemaScript> LoadScripts()
    {
        var assembly = typeof(DatabaseMigrator).Assembly;
        var scripts = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(n => Load(assembly, n))
            .OrderBy(s => s.Version)
            .ToList();

        var duplicate = scripts.GroupBy(s => s.Version).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Two schema scripts share version {duplicate.Key}: {string.Join(", ", duplicate.Select(s => s.Name))}.");
        }

        return scripts;
    }

    private static SchemaScript Load(Assembly assembly, string resource)
    {
        var name = resource[ResourcePrefix.Length..];
        var match = ScriptName().Match(name);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Schema script '{name}' must be named NNN_description.sql.");
        }

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return new SchemaScript(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), name, reader.ReadToEnd());
    }

    [GeneratedRegex(@"^(\d{3,})_[A-Za-z0-9_]+\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptName();

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Applying database script {Version} ({Name})")]
    private partial void LogApplying(int version, string name);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Database schema at version {Version} ({Applied} script(s) applied now)")]
    private partial void LogUpToDate(int version, int applied);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Critical, Message = "Database script {Version} ({Name}) failed: nothing of it was applied")]
    private partial void LogFailed(Exception exception, int version, string name);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning, Message = "Database script {Version} ({Name}) was changed after being applied: write a new script instead")]
    private partial void LogChecksumMismatch(int version, string name);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Warning, Message = "Database schema version {Version} is unknown to this build of the application")]
    private partial void LogUnknownVersion(int version);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Warning, Message = "Database not reachable (attempt {Attempt}), retrying in {Delay}")]
    private partial void LogRetrying(Exception exception, int attempt, TimeSpan delay);
}
