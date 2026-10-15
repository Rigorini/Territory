using System.Text.RegularExpressions;

namespace Territory.Api;

/// <summary>
/// Only files whose name carries a content hash can be cached forever; anything else (index.html, i18n, favicon)
/// must be revalidated, otherwise browsers keep stale copies after a deployment.
/// </summary>
public static partial class StaticFileCachePolicy
{
    public const string Revalidate = "no-cache";
    public const string Immutable = "public, max-age=31536000, immutable";

    public static string For(string fileName) => HashedName().IsMatch(fileName) ? Immutable : Revalidate;

    // Angular appends an 8-character content hash to bundles, chunks, styles and media (e.g. main-SNBGSEDY.js).
    [GeneratedRegex(@"-[A-Za-z0-9_-]{8}\.(?:js|css|woff2?)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex HashedName();
}
