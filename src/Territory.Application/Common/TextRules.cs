namespace Territory.Application.Common;

/// <summary>Shared field error codes and validation of single-line text input.</summary>
public static class TextRules
{
    public const string Required = "required";
    public const string MaxLength = "maxLength";
    public const string InvalidFormat = "invalidFormat";
    public const string OutOfRange = "outOfRange";
    public const string NotFound = "notFound";

    /// <summary>Trims a required single-line text and validates length and control characters.</summary>
    /// <returns>The trimmed text, or null when missing.</returns>
    public static string? RequiredText(ValidationErrors errors, string field, string? value, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors.Add(field, Required);
            return null;
        }

        if (text.Length > maxLength)
        {
            errors.Add(field, MaxLength);
        }
        else if (text.Any(char.IsControl))
        {
            errors.Add(field, InvalidFormat);
        }

        return text;
    }

    /// <summary>Case-insensitive key used for uniqueness checks, aligned with lower() in the SQL indexes.</summary>
    public static string Normalize(string value) => value.ToLowerInvariant();
}
