using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Territory.Application.Abstractions;
using Territory.Application.Common;
using Territory.Application.Domain;

namespace Territory.Application.Export;

/// <summary>Query string of the export; dates are local days (yyyy-MM-dd) in TimeZone (IANA id, UTC when unknown).</summary>
public sealed record RegisterExportRequest(
    string? Format, int? MunicipalityId, string? From, string? To, string? TimeZone, string? Language);

public sealed record ExportFile(string FileName, string ContentType, byte[] Content);

public sealed partial class RegisterExportService(
    ITerritoryDbContext db,
    IEnumerable<IRegisterWriter> writers,
    TimeProvider clock,
    ILogger<RegisterExportService> logger)
{
    public const string FormatField = "format";
    public const string MunicipalityField = "municipalityId";
    public const string FromField = "from";
    public const string ToField = "to";
    public const string FileNamePrefix = "registro-territori-";
    public const int MaxTimeZoneLength = 64;

    private const string DayFormat = "yyyy-MM-dd";

    private sealed record ValidRequest(
        IRegisterWriter Writer, string? MunicipalityName, int? MunicipalityId, DateOnly? From, DateOnly? To, TimeZoneInfo TimeZone);

    private sealed record TerritoryRow(
        int Id, string MunicipalityName, int Number, string Name, string? CurrentPublisher, DateTimeOffset? AssignedAt, DateTimeOffset? LastReturnedAt);

    private sealed record TerritoryQueryRow(
        int Id, string MunicipalityName, int Number, string Name, string? PublisherFirstName, string? PublisherLastName,
        DateTimeOffset? AssignedAt, DateTimeOffset? LastReturnedAt);

    private sealed record AssignmentQueryRow(
        int TerritoryId, string PublisherFirstName, string PublisherLastName, DateTimeOffset AssignedAt, DateTimeOffset? ReturnedAt,
        ReturnedBy? ReturnedBy, string? ReturnedByName);

    private sealed record AssignmentRow(
        int TerritoryId, string PublisherName, DateTimeOffset AssignedAt, DateTimeOffset? ReturnedAt, ReturnedBy? ReturnedBy,
        string? ReturnedByName);

    public async Task<Result<ExportFile>> ExportAsync(RegisterExportRequest request, int actorId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(request, cancellationToken);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        var valid = validation.Value!;
        var report = await BuildReportAsync(valid, RegisterTexts.For(request.Language), cancellationToken);
        using var output = new MemoryStream();
        valid.Writer.Write(report, output);
        LogExported(actorId, valid.Writer.Format, report.Territories.Count);
        return new ExportFile(
            $"{FileNamePrefix}{report.GeneratedOn.ToString(DayFormat, CultureInfo.InvariantCulture)}.{valid.Writer.Format}",
            valid.Writer.ContentType,
            output.ToArray());
    }

    private async Task<Result<ValidRequest>> ValidateAsync(RegisterExportRequest request, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var writer = writers.FirstOrDefault(w => string.Equals(w.Format, request.Format, StringComparison.Ordinal));
        errors.AddIf(writer is null, FormatField, request.Format is null ? TextRules.Required : TextRules.InvalidFormat);
        var from = ParseDay(errors, FromField, request.From);
        var to = ParseDay(errors, ToField, request.To);
        errors.AddIf(from > to, ToField, TextRules.OutOfRange);
        var municipalityName = await FindMunicipalityAsync(errors, request.MunicipalityId, cancellationToken);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        return new ValidRequest(writer!, municipalityName, request.MunicipalityId, from, to, ResolveTimeZone(request.TimeZone));
    }

    private static DateOnly? ParseDay(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(value, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            errors.Add(field, TextRules.InvalidFormat);
            return null;
        }

        errors.AddIf(day < DateOnly.FromDateTime(DateRules.MinDate.UtcDateTime), field, TextRules.OutOfRange);
        return day;
    }

    private async Task<string?> FindMunicipalityAsync(ValidationErrors errors, int? municipalityId, CancellationToken cancellationToken)
    {
        if (municipalityId is null)
        {
            return null;
        }

        var name = await db.Municipalities
            .Where(m => m.Id == municipalityId)
            .Select(m => m.Name)
            .SingleOrDefaultAsync(cancellationToken);
        errors.AddIf(name is null, MunicipalityField, TextRules.NotFound);
        return name;
    }

    public static TimeZoneInfo ResolveTimeZone(string? id) =>
        id is { Length: > 0 and <= MaxTimeZoneLength } && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
            ? zone
            : TimeZoneInfo.Utc;

    /// <summary>Start of the given local day as an UTC instant; days starting in a DST gap begin at the first valid time.</summary>
    public static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    private static DateOnly? LocalDay(DateTimeOffset? instant, TimeZoneInfo zone) =>
        instant is { } value ? LocalDay(value, zone) : null;

    private async Task<RegisterReport> BuildReportAsync(ValidRequest valid, RegisterTexts texts, CancellationToken cancellationToken)
    {
        var territories = (await QueryTerritoriesAsync(valid.MunicipalityId, cancellationToken))
            .Select(t => new TerritoryRow(
                t.Id, t.MunicipalityName, t.Number, t.Name, FullName(t.PublisherFirstName, t.PublisherLastName), t.AssignedAt, t.LastReturnedAt))
            .ToList();
        var assignments = (await QueryAssignmentsAsync(valid, cancellationToken))
            .Select(a => new AssignmentRow(
                a.TerritoryId, FullName(a.PublisherFirstName, a.PublisherLastName)!, a.AssignedAt, a.ReturnedAt, a.ReturnedBy, a.ReturnedByName))
            .ToList();
        var zone = valid.TimeZone;
        var entries = assignments.ToLookup(
            a => a.TerritoryId,
            a => new RegisterEntry(
                a.PublisherName, LocalDay(a.AssignedAt, zone), LocalDay(a.ReturnedAt, zone), a.ReturnedBy, a.ReturnedByName));
        var rows = territories
            .Select(t => new RegisterTerritory(
                t.MunicipalityName,
                t.Number,
                t.Name,
                t.CurrentPublisher,
                LocalDay(t.AssignedAt ?? t.LastReturnedAt, zone),
                [.. entries[t.Id]]))
            .ToList();
        return new RegisterReport(texts, LocalDay(clock.GetUtcNow(), zone), valid.MunicipalityName, valid.From, valid.To, rows);
    }

    // Names are encrypted in the database: they are read as separate columns and combined here.
    private static string? FullName(string? firstName, string? lastName) =>
        firstName is null || lastName is null ? null : $"{lastName} {firstName}";

    private Task<List<TerritoryQueryRow>> QueryTerritoriesAsync(int? municipalityId, CancellationToken cancellationToken) =>
        db.Territories
            .AsNoTracking()
            .Where(t => municipalityId == null || t.MunicipalityId == municipalityId)
            .OrderBy(t => t.Municipality!.Name)
            .ThenBy(t => t.Number)
            .Select(t => new TerritoryQueryRow(
                t.Id,
                t.Municipality!.Name,
                t.Number,
                t.Name,
                t.Assignments.Where(a => a.ReturnedAt == null).Select(a => a.Publisher!.FirstName).FirstOrDefault(),
                t.Assignments.Where(a => a.ReturnedAt == null).Select(a => a.Publisher!.LastName).FirstOrDefault(),
                t.Assignments.Where(a => a.ReturnedAt == null).Select(a => (DateTimeOffset?)a.AssignedAt).FirstOrDefault(),
                t.Assignments.Max(a => a.ReturnedAt)))
            .ToListAsync(cancellationToken);

    /// <summary>Assignments overlapping [From, To]: assigned before the end and not returned before the start.</summary>
    private Task<List<AssignmentQueryRow>> QueryAssignmentsAsync(ValidRequest valid, CancellationToken cancellationToken)
    {
        DateTimeOffset? start = valid.From is { } from ? StartOfDay(from, valid.TimeZone) : null;
        DateTimeOffset? end = valid.To is { } to ? StartOfDay(to.AddDays(1), valid.TimeZone) : null;
        return db.Assignments
            .AsNoTracking()
            .Where(a => valid.MunicipalityId == null || a.Territory!.MunicipalityId == valid.MunicipalityId)
            .Where(a => start == null || a.ReturnedAt == null || a.ReturnedAt >= start)
            .Where(a => end == null || a.AssignedAt < end)
            .OrderBy(a => a.AssignedAt)
            .ThenBy(a => a.Id)
            .Select(a => new AssignmentQueryRow(
                a.TerritoryId, a.Publisher!.FirstName, a.Publisher.LastName, a.AssignedAt, a.ReturnedAt, a.ReturnedBy,
                a.ReturnedByName))
            .ToListAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {ActorId} exported the register as {Format} ({TerritoryCount} territories)")]
    private partial void LogExported(int actorId, string format, int territoryCount);
}
