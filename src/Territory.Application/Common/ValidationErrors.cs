namespace Territory.Application.Common;

/// <summary>Collects field error codes (e.g. "username" → ["required"]) for a validation result.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    public bool HasErrors => errors.Count > 0;

    public ValidationErrors Add(string field, string code)
    {
        if (!errors.TryGetValue(field, out var codes))
        {
            codes = [];
            errors[field] = codes;
        }

        codes.Add(code);
        return this;
    }

    public ValidationErrors AddIf(bool condition, string field, string code) =>
        condition ? Add(field, code) : this;

    public AppError ToError() =>
        AppError.Validation(errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
}
