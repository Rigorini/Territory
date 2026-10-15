using Microsoft.Extensions.Configuration;
using Territory.Infrastructure;
using Territory.Infrastructure.Security;

namespace Territory.Api.Cli;

/// <summary>Operational commands for the personal-data encryption key (see README, "Personal data encryption").</summary>
public static class PersonalDataCommands
{
    public const string GenerateKeyName = "generate-personal-data-key";
    public const string ProtectName = "protect-personal-data";
    public const string EncryptExistingName = "encrypt-existing-personal-data";

    public static bool IsCommand(string? name) => name is GenerateKeyName or ProtectName or EncryptExistingName;

    public static Task<int> RunAsync(
        string[] args, IConfiguration configuration, string contentRoot, TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
        args[0] switch
        {
            GenerateKeyName => Task.FromResult(GenerateKey(args.ElementAtOrDefault(1), contentRoot, output, error)),
            ProtectName => Task.FromResult(Protect(configuration, contentRoot, Console.In.ReadLine, output, error)),
            _ => EncryptExistingAsync(configuration, contentRoot, output, error, cancellationToken)
        };

    /// <summary>
    /// Without an argument the key is printed; with <c>--file</c> it is written to the default key file,
    /// with any other argument to that path. An existing file is never overwritten.
    /// </summary>
    public static int GenerateKey(string? target, string contentRoot, TextWriter output, TextWriter error)
    {
        if (target is null)
        {
            output.WriteLine(AesGcmPersonalDataProtector.GenerateKey());
            return 0;
        }

        var path = Path.GetFullPath(
            target == "--file" ? AesGcmPersonalDataProtector.DefaultKeyFile : target, contentRoot);
        try
        {
            AesGcmPersonalDataProtector.CreateKeyFile(path);
        }
        catch (IOException) when (File.Exists(path))
        {
            error.WriteLine($"Il file '{path}' esiste gia': non viene sovrascritto (sostituire la chiave renderebbe i dati illeggibili).");
            return 1;
        }

        output.WriteLine($"Chiave creata in '{path}'. Eseguire un backup in un luogo sicuro e separato dal database.");
        return 0;
    }

    /// <summary>Encrypts one value read from stdin, e.g. the display name.</summary>
    public static int Protect(IConfiguration configuration, string contentRoot, Func<string?> readLine, TextWriter output, TextWriter error)
    {
        if (!TryCreateProtector(configuration, contentRoot, error, out var protector))
        {
            return 1;
        }

        var value = readLine();
        if (string.IsNullOrWhiteSpace(value))
        {
            error.WriteLine("Valore mancante.");
            return 1;
        }

        output.WriteLine(protector.Protect(value.Trim()));
        return 0;
    }

    public static async Task<int> EncryptExistingAsync(
        IConfiguration configuration, string contentRoot, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString(DependencyInjection.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error.WriteLine($"Manca ConnectionStrings:{DependencyInjection.ConnectionStringName}.");
            return 1;
        }

        if (!TryCreateProtector(configuration, contentRoot, error, out var protector))
        {
            return 1;
        }

        var result = await PersonalDataMigration.RunAsync(connectionString, protector, cancellationToken);
        output.WriteLine($"Cifrati: {result.Publishers} proclamatori, {result.Admins} amministratori, {result.Assignments} riconsegne.");
        return 0;
    }

    private static bool TryCreateProtector(
        IConfiguration configuration, string contentRoot, TextWriter error, out AesGcmPersonalDataProtector protector)
    {
        try
        {
            protector = AesGcmPersonalDataProtector.FromConfiguration(configuration, contentRoot);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine(ex.Message);
            protector = null!;
            return false;
        }
    }
}
