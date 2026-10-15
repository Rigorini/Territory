using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Territory.Application.Common;

namespace Territory.Application.Territories;

/// <summary>
/// Accepts only Google MyMaps links and keeps just their "mid": the server never requests a user-supplied URL (SSRF).
/// </summary>
public static partial class MyMapsLink
{
    public const int UrlMaxLength = 2048;
    private const string MapIdParameter = "mid";

    private static readonly FrozenSet<string> AllowedHosts =
        FrozenSet.ToFrozenSet(["www.google.com", "google.com", "www.google.it", "google.it"], StringComparer.Ordinal);

    public static string ToViewerUrl(string mapId) =>
        $"https://www.google.com/maps/d/viewer?{MapIdParameter}={Uri.EscapeDataString(mapId)}";

    /// <returns>The map id, or null (with a field error) when the link is missing or not an accepted MyMaps URL.</returns>
    public static string? Validate(ValidationErrors errors, string field, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            errors.Add(field, TextRules.Required);
            return null;
        }

        var mapId = TryExtractMapId(url);
        errors.AddIf(mapId is null, field, TextRules.InvalidFormat);
        return mapId;
    }

    public static string? TryExtractMapId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > UrlMaxLength)
        {
            return null;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || !IsMyMapsUri(uri))
        {
            return null;
        }

        var mapId = ReadQueryValue(uri.Query, MapIdParameter);
        return IsValidMapId(mapId) ? mapId : null;
    }

    public static bool IsValidMapId(string? mapId) => mapId is not null && MapIdPattern().IsMatch(mapId);

    private static bool IsMyMapsUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && uri.UserInfo.Length == 0
        && AllowedHosts.Contains(uri.Host)
        && uri.AbsolutePath.StartsWith("/maps/d/", StringComparison.Ordinal);

    private static string? ReadQueryValue(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator > 0 && pair.AsSpan(0, separator).SequenceEqual(name))
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return null;
    }

    // MyMaps ids are URL-safe tokens; the length is bounded by territory.mymaps_id.
    [GeneratedRegex("^[A-Za-z0-9_-]{10,100}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex MapIdPattern();
}
