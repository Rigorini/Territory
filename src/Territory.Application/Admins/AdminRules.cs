using System.Text.RegularExpressions;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Security;

namespace Territory.Application.Admins;

public static partial class AdminRules
{
    public const string UsernameField = "username";
    public const string PasswordField = "password";
    public const string DisplayNameField = "displayName";

    /// <returns>The trimmed name, or null when missing.</returns>
    public static string? ValidateDisplayName(ValidationErrors errors, string? displayName) =>
        TextRules.RequiredText(errors, DisplayNameField, displayName, AdminUser.DisplayNameMaxLength);

    public static void ValidateUsername(ValidationErrors errors, string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            errors.Add(UsernameField, "required");
            return;
        }

        errors.AddIf(!UsernamePattern().IsMatch(username), UsernameField, "invalidFormat");
    }

    public const string RoleField = "role";

    public static void ValidateRole(ValidationErrors errors, AdminRole? role) =>
        errors.AddIf(role is { } value && !Enum.IsDefined(value), RoleField, TextRules.InvalidFormat);

    public static void ValidatePassword(ValidationErrors errors, string? password)
    {
        foreach (var rule in PasswordPolicy.Validate(password))
        {
            errors.Add(PasswordField, ToCode(rule));
        }
    }

    public static string ToCode(PasswordRule rule)
    {
        var name = rule.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    // Allowlist: letters, digits, dot, underscore, dash; 3-50 chars (matches users.username).
    [GeneratedRegex("^[A-Za-z0-9._-]{3,50}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex UsernamePattern();
}
