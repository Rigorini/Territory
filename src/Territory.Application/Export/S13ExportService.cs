using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Territory.Application.Abstractions;
using Territory.Application.Common;

namespace Territory.Application.Export;

/// <summary>Query string of the S-13 export; the service year is identified by the year in which it ends.</summary>
public sealed record S13ExportRequest(int? ServiceYear, string? TimeZone, string? Language);

public sealed partial class S13ExportService(
    ITerritoryDbContext db,
    IS13Writer writer,
    ILogger<S13ExportService> logger)
{
    public const string ServiceYearField = "serviceYear";
    public const string FileNamePrefix = "S-13-";
    public const int MinServiceYear = 2001;
    public const int MaxServiceYear = 2100;

    /// <summary>The service year N runs from September 1st of N-1 to August 31st of N.</summary>
    private const int FirstMonth = 9;

    private sealed record TerritoryQueryRow(int Id, string MunicipalityName, int Number);

    private sealed record AssignmentQueryRow(
        int TerritoryId, string FirstName, string LastName, DateTimeOffset AssignedAt, DateTimeOffset? ReturnedAt);

    private sealed record LastReturnRow(int TerritoryId, DateTimeOffset ReturnedAt);

    public async Task<Result<ExportFile>> ExportAsync(S13ExportRequest request, int actorId, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        errors.AddIf(request.ServiceYear is null, ServiceYearField, TextRules.Required);
        errors.AddIf(
            request.ServiceYear is { } year && year is < MinServiceYear or > MaxServiceYear, ServiceYearField, TextRules.OutOfRange);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var serviceYear = request.ServiceYear!.Value;
        var language = Languages.IsSupported(request.Language) ? request.Language! : Languages.English;
        var report = await BuildReportAsync(
            serviceYear, language, RegisterExportService.ResolveTimeZone(request.TimeZone), cancellationToken);
        using var output = new MemoryStream();
        writer.Write(report, output);
        LogExported(actorId, serviceYear, report.Municipalities.Count);
        return new ExportFile(
            $"{FileNamePrefix}{serviceYear.ToString(CultureInfo.InvariantCulture)}-{language}.docx", writer.ContentType, output.ToArray());
    }

    private async Task<S13Report> BuildReportAsync(int serviceYear, string language, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var start = RegisterExportService.StartOfDay(new DateOnly(serviceYear - 1, FirstMonth, 1), zone);
        var end = RegisterExportService.StartOfDay(new DateOnly(serviceYear, FirstMonth, 1), zone);
        var territories = await QueryTerritoriesAsync(cancellationToken);
        var assignments = (await QueryAssignmentsAsync(start, end, cancellationToken)).ToLookup(a => a.TerritoryId);
        var lastReturns = (await QueryLastReturnsAsync(start, cancellationToken)).ToDictionary(r => r.TerritoryId, r => r.ReturnedAt);

        var municipalities = territories
            .GroupBy(t => t.MunicipalityName, StringComparer.Ordinal)
            .Select(group => new S13Municipality(
                group.Key,
                [.. group.Select(t => new S13Territory(
                    t.Number,
                    lastReturns.TryGetValue(t.Id, out var returned) ? LocalDay(returned, zone) : null,
                    [.. assignments[t.Id].Select(a => ToAssignment(a, zone))]))]))
            .ToList();
        return new S13Report(language, serviceYear, municipalities);
    }

    private static S13Assignment ToAssignment(AssignmentQueryRow row, TimeZoneInfo zone) =>
        new(
            $"{row.FirstName} {row.LastName}",
            LocalDay(row.AssignedAt, zone),
            row.ReturnedAt is { } returned ? LocalDay(returned, zone) : null);

    private static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    private Task<List<TerritoryQueryRow>> QueryTerritoriesAsync(CancellationToken cancellationToken) =>
        db.Territories
            .AsNoTracking()
            .OrderBy(t => t.Municipality!.Name)
            .ThenBy(t => t.Number)
            .Select(t => new TerritoryQueryRow(t.Id, t.Municipality!.Name, t.Number))
            .ToListAsync(cancellationToken);

    /// <summary>A registration belongs to the service year of its assignment date, whatever its return date.</summary>
    private Task<List<AssignmentQueryRow>> QueryAssignmentsAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken) =>
        db.Assignments
            .AsNoTracking()
            .Where(a => a.AssignedAt >= start && a.AssignedAt < end)
            .OrderBy(a => a.AssignedAt)
            .ThenBy(a => a.Id)
            .Select(a => new AssignmentQueryRow(a.TerritoryId, a.Publisher!.FirstName, a.Publisher.LastName, a.AssignedAt, a.ReturnedAt))
            .ToListAsync(cancellationToken);

    /// <summary>Last return among the assignments made before the service year (those of previous sheets).</summary>
    private Task<List<LastReturnRow>> QueryLastReturnsAsync(DateTimeOffset start, CancellationToken cancellationToken) =>
        db.Assignments
            .AsNoTracking()
            .Where(a => a.AssignedAt < start && a.ReturnedAt != null)
            .GroupBy(a => a.TerritoryId)
            .Select(g => new LastReturnRow(g.Key, g.Max(a => a.ReturnedAt)!.Value))
            .ToListAsync(cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {ActorId} exported the S-13 register of service year {ServiceYear} ({MunicipalityCount} municipalities)")]
    private partial void LogExported(int actorId, int serviceYear, int municipalityCount);
}
