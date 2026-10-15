namespace Territory.Application.Security;

public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    public static IReadOnlyList<PasswordRule> Validate(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return [PasswordRule.Required];
        }

        var violations = new List<PasswordRule>();
        AddIf(violations, password.Length < MinLength, PasswordRule.TooShort);
        AddIf(violations, password.Length > MaxLength, PasswordRule.TooLong);
        AddIf(violations, !password.Any(char.IsUpper), PasswordRule.MissingUppercase);
        AddIf(violations, !password.Any(char.IsLower), PasswordRule.MissingLowercase);
        AddIf(violations, !password.Any(char.IsDigit), PasswordRule.MissingDigit);
        AddIf(violations, password.All(char.IsLetterOrDigit), PasswordRule.MissingSymbol);
        return violations;
    }

    private static void AddIf(List<PasswordRule> violations, bool condition, PasswordRule rule)
    {
        if (condition)
        {
            violations.Add(rule);
        }
    }
}

public enum PasswordRule
{
    Required,
    TooShort,
    TooLong,
    MissingUppercase,
    MissingLowercase,
    MissingDigit,
    MissingSymbol
}
