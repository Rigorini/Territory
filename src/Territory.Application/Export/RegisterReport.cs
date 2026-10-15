using Territory.Application.Domain;

namespace Territory.Application.Export;

/// <summary>One assignment of the register; dates are local days in the requested time zone.</summary>
public sealed record RegisterEntry(
    string PublisherName, DateOnly AssignedOn, DateOnly? ReturnedOn, ReturnedBy? ReturnedBy, string? ReturnedByName = null);

/// <summary>
/// A territory with its current status and the assignments overlapping the requested period.
/// Since is the assignment day when assigned, otherwise the last return day (null if never assigned).
/// </summary>
public sealed record RegisterTerritory(
    string MunicipalityName,
    int Number,
    string Name,
    string? CurrentPublisher,
    DateOnly? Since,
    IReadOnlyList<RegisterEntry> Entries)
{
    public bool IsAssigned => CurrentPublisher is not null;
}

/// <summary>Format-independent content of the exported register.</summary>
public sealed record RegisterReport(
    RegisterTexts Texts,
    DateOnly GeneratedOn,
    string? MunicipalityName,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<RegisterTerritory> Territories)
{
    /// <summary>One row per assignment; territories without assignments in the period get a single row with a null entry.</summary>
    public IEnumerable<(RegisterTerritory Territory, RegisterEntry? Entry)> Rows() =>
        Territories.SelectMany(t => t.Entries.Count == 0
            ? [(t, (RegisterEntry?)null)]
            : t.Entries.Select(e => (t, (RegisterEntry?)e)));

    public IEnumerable<IGrouping<string, RegisterTerritory>> ByMunicipality() =>
        Territories.GroupBy(t => t.MunicipalityName, StringComparer.Ordinal);
}

/// <summary>Renders a register report to a downloadable file.</summary>
public interface IRegisterWriter
{
    /// <summary>Format identifier, also used as file extension (e.g. "xlsx").</summary>
    string Format { get; }

    string ContentType { get; }

    void Write(RegisterReport report, Stream output);
}
