using System.Globalization;
using Territory.Application.Common;
using Territory.Application.Domain;

namespace Territory.Application.Export;

/// <summary>Localized labels of the exported register (the export is generated server-side, outside the SPA i18n).</summary>
public sealed record RegisterTexts(
    CultureInfo Culture,
    string Title,
    string StatusSheet,
    string RegisterSheet,
    string Municipality,
    string Number,
    string Territory,
    string Status,
    string Publisher,
    string Since,
    string AssignedOn,
    string ReturnedOn,
    string ReturnedBy,
    string Assigned,
    string Available,
    string ByPublisher,
    string ByAdmin,
    string AllMunicipalities,
    string WholeHistory,
    string PeriodFromTo,
    string PeriodFrom,
    string PeriodTo,
    string GeneratedOn,
    string NoAssignments)
{
    public static readonly RegisterTexts Italian = new(
        CultureInfo.GetCultureInfo("it-IT"),
        Title: "Registro dei territori",
        StatusSheet: "Stato",
        RegisterSheet: "Registro",
        Municipality: "Comune",
        Number: "N°",
        Territory: "Territorio",
        Status: "Stato",
        Publisher: "Proclamatore",
        Since: "Dal",
        AssignedOn: "Assegnato il",
        ReturnedOn: "Riconsegnato il",
        ReturnedBy: "Riconsegnato da",
        Assigned: "Assegnato",
        Available: "Disponibile",
        ByPublisher: "Proclamatore",
        ByAdmin: "Amministratore",
        AllMunicipalities: "Tutti i comuni",
        WholeHistory: "Tutto lo storico",
        PeriodFromTo: "Periodo: dal {0} al {1}",
        PeriodFrom: "Periodo: dal {0}",
        PeriodTo: "Periodo: fino al {0}",
        GeneratedOn: "Generato il {0}",
        NoAssignments: "Nessuna assegnazione nel periodo");

    public static readonly RegisterTexts English = new(
        CultureInfo.GetCultureInfo("en-GB"),
        Title: "Territory register",
        StatusSheet: "Status",
        RegisterSheet: "Register",
        Municipality: "Municipality",
        Number: "No.",
        Territory: "Territory",
        Status: "Status",
        Publisher: "Publisher",
        Since: "Since",
        AssignedOn: "Assigned on",
        ReturnedOn: "Returned on",
        ReturnedBy: "Returned by",
        Assigned: "Assigned",
        Available: "Available",
        ByPublisher: "Publisher",
        ByAdmin: "Administrator",
        AllMunicipalities: "All municipalities",
        WholeHistory: "Whole history",
        PeriodFromTo: "Period: from {0} to {1}",
        PeriodFrom: "Period: from {0}",
        PeriodTo: "Period: until {0}",
        GeneratedOn: "Generated on {0}",
        NoAssignments: "No assignments in the period");

    /// <summary>Italian is the congregation language and the fallback.</summary>
    public static RegisterTexts For(string? language) =>
        language == Languages.English ? English : Italian;

    /// <summary>Same dd/MM/yyyy layout in every language, independent of the culture data installed on the host.</summary>
    public const string DayFormat = "dd'/'MM'/'yyyy";

    public string Date(DateOnly? value) => value?.ToString(DayFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    public string StatusOf(RegisterTerritory territory) => territory.IsAssigned ? Assigned : Available;

    /// <summary>The admin's name when recorded, otherwise the generic label.</summary>
    public string ReturnedByText(ReturnedBy? value, string? adminName = null) => value switch
    {
        Domain.ReturnedBy.Publisher => ByPublisher,
        Domain.ReturnedBy.Admin => string.IsNullOrEmpty(adminName) ? ByAdmin : adminName,
        _ => string.Empty
    };

    public string Period(DateOnly? from, DateOnly? to) => (from, to) switch
    {
        (null, null) => WholeHistory,
        (not null, not null) => Format(PeriodFromTo, Date(from), Date(to)),
        (not null, null) => Format(PeriodFrom, Date(from)),
        _ => Format(PeriodTo, Date(to))
    };

    public string Generated(DateOnly day) => Format(GeneratedOn, Date(day));

    public string Scope(string? municipalityName) => municipalityName ?? AllMunicipalities;

    private string Format(string pattern, params object[] args) => string.Format(Culture, pattern, args);
}
