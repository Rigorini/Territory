using Microsoft.AspNetCore.Identity;
using Territory.Application.Domain;
using Territory.Application.Security;

namespace Territory.Api.Cli;

/// <summary>
/// Generates the password hash for a manually created admin.
/// The password is read from stdin (never from arguments) to keep it out of shell history.
/// </summary>
public static class HashPasswordCommand
{
    public const string Name = "hash-password";

    public static int Run(TextWriter output, TextWriter error, Func<string?> readSecret)
    {
        output.Write("Password: ");
        var password = readSecret();
        output.WriteLine();

        var violations = PasswordPolicy.Validate(password);
        if (violations.Count > 0)
        {
            error.WriteLine($"Password non valida: {string.Join(", ", violations)}");
            return 1;
        }

        output.Write("Conferma password: ");
        var confirmation = readSecret();
        output.WriteLine();
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            error.WriteLine("Le password non coincidono.");
            return 1;
        }

        var hasher = new PasswordHasher<AdminUser>();
        output.WriteLine(hasher.HashPassword(user: null!, password!));
        return 0;
    }

    public static string? ReadConsoleSecret()
    {
        if (Console.IsInputRedirected)
        {
            return Console.In.ReadLine();
        }

        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
                continue;
            }

            if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }
    }
}
