using Microsoft.Extensions.Logging.Abstractions;
using Territory.Infrastructure;
using Territory.Infrastructure.Persistence;

namespace Territory.Api.Cli;

/// <summary>Updates the database schema on demand (see README, "Database setup"), e.g. with the owner account.</summary>
public static class DatabaseCommands
{
    public const string MigrateName = "migrate-database";

    public static bool IsCommand(string? name) => name == MigrateName;

    public static async Task<int> RunAsync(
        IConfiguration configuration, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString(DependencyInjection.MigrationConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration.GetConnectionString(DependencyInjection.ConnectionStringName);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error.WriteLine(
                $"Configurare 'ConnectionStrings:{DependencyInjection.MigrationConnectionStringName}' " +
                $"o 'ConnectionStrings:{DependencyInjection.ConnectionStringName}'.");
            return 1;
        }

        var migrator = new DatabaseMigrator(connectionString, NullLogger<DatabaseMigrator>.Instance, TimeProvider.System);
        try
        {
            var result = await migrator.MigrateAsync(cancellationToken);
            foreach (var script in result.Applied)
            {
                output.WriteLine($"Applicato {script.Name}");
            }

            output.WriteLine($"Database alla versione {result.CurrentVersion}.");
            return 0;
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException)
        {
            error.WriteLine($"Aggiornamento del database non riuscito: {ex.Message}");
            return 1;
        }
    }
}
