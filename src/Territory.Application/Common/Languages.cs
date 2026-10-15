using System.Collections.Frozen;

namespace Territory.Application.Common;

/// <summary>User interface languages; must match SUPPORTED_LANGUAGES in the Angular client.</summary>
public static class Languages
{
    public const string English = "en";
    public const string Italian = "it";

    private static readonly FrozenSet<string> Supported = FrozenSet.ToFrozenSet([English, Italian], StringComparer.Ordinal);

    public static bool IsSupported(string? language) => language is not null && Supported.Contains(language);
}
