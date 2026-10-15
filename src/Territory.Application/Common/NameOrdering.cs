using System.Globalization;

namespace Territory.Application.Common;

/// <summary>Names are encrypted in the database, so ordering happens in memory with a fixed, culture-independent rule.</summary>
public static class NameOrdering
{
    public static readonly StringComparer Comparer = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);
}
