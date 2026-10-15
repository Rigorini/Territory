using Npgsql;
using Territory.Application.Abstractions;
using Territory.Application.Publishers;

namespace Territory.Infrastructure.Security;

/// <summary>
/// One-off, idempotent conversion of plain-text names written before encryption was introduced.
/// Works on raw SQL because the EF mapping would refuse to read a plain-text value.
/// </summary>
public static class PersonalDataMigration
{
    public sealed record Result(int Publishers, int Admins, int Assignments);

    public static async Task<Result> RunAsync(string connectionString, IPersonalDataCipher cipher, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var publishers = await EncryptPublishersAsync(connection, transaction, cipher, cancellationToken);
        var admins = await EncryptColumnAsync(connection, transaction, cipher, "users", "display_name", cancellationToken);
        var assignments = await EncryptColumnAsync(connection, transaction, cipher, "assignment", "returned_by_name", cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new Result(publishers, admins, assignments);
    }

    private static bool NeedsEncryption(string? value) =>
        value is not null && !AesGcmPersonalDataProtector.IsProtected(value);

    private static async Task<int> EncryptPublishersAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IPersonalDataCipher cipher, CancellationToken cancellationToken)
    {
        var rows = new List<(int Id, string First, string Last, string Hash)>();
        await using (var select = new NpgsqlCommand("SELECT id, first_name, last_name, name_hash FROM publisher", connection, transaction))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        var updated = 0;
        foreach (var (id, first, last, hash) in rows)
        {
            var plainFirst = NeedsEncryption(first) ? first : cipher.Unprotect(first);
            var plainLast = NeedsEncryption(last) ? last : cipher.Unprotect(last);
            var newHash = cipher.BlindIndex(PublisherService.NameIdentity(plainFirst, plainLast));
            if (!NeedsEncryption(first) && !NeedsEncryption(last) && hash == newHash)
            {
                continue;
            }

            await using var update = new NpgsqlCommand(
                "UPDATE publisher SET first_name = @first, last_name = @last, name_hash = @hash WHERE id = @id", connection, transaction);
            update.Parameters.AddWithValue("first", NeedsEncryption(first) ? cipher.Protect(first) : first);
            update.Parameters.AddWithValue("last", NeedsEncryption(last) ? cipher.Protect(last) : last);
            update.Parameters.AddWithValue("hash", newHash);
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
            updated++;
        }

        return updated;
    }

    // table and column are constants chosen by this class, never user input.
    private static async Task<int> EncryptColumnAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IPersonalDataCipher cipher,
        string table, string column, CancellationToken cancellationToken)
    {
        var rows = new List<(int Id, string Value)>();
        await using (var select = new NpgsqlCommand($"SELECT id, {column} FROM {table} WHERE {column} IS NOT NULL", connection, transaction))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetInt32(0), reader.GetString(1)));
            }
        }

        var updated = 0;
        foreach (var (id, value) in rows.Where(r => NeedsEncryption(r.Value)))
        {
            await using var update = new NpgsqlCommand($"UPDATE {table} SET {column} = @value WHERE id = @id", connection, transaction);
            update.Parameters.AddWithValue("value", cipher.Protect(value));
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
            updated++;
        }

        return updated;
    }
}
