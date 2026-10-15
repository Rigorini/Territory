using Microsoft.Extensions.Logging.Abstractions;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Export;
using Territory.Infrastructure.Persistence;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class S13ExportServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Rome = "Europe/Rome";
    private readonly CapturingWriter writer = new();
    private TerritoryDbContext db = null!;
    private S13ExportService service = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        db = fixture.CreateDbContext();
        service = new S13ExportService(db, writer, NullLogger<S13ExportService>.Instance);
    }

    public ValueTask DisposeAsync() => db.DisposeAsync();

    [Fact]
    public async Task ServiceYear_GroupsByAssignmentDateNotByReturnDate()
    {
        var giano = await fixture.SeedMunicipalityAsync("Giano");
        var territory = await fixture.SeedTerritoryAsync(giano.Id, 1);
        var rossi = await fixture.SeedPublisherAsync("Mario", "Rossi");
        // Assigned before the service year 2026 (Sep 2025 - Aug 2026) and returned inside it: belongs to 2025.
        await fixture.SeedHistoryAsync(territory.Id, rossi.Id, Utc(2025, 8, 20), Utc(2025, 10, 5));
        // 31 August 22:30 UTC is already 1 September in Rome: first day of the service year 2026.
        await fixture.SeedHistoryAsync(territory.Id, rossi.Id, Utc(2025, 8, 31, 22, 30), Utc(2025, 11, 1));
        // Assigned inside the year and still open.
        await fixture.SeedHistoryAsync(territory.Id, rossi.Id, Utc(2026, 8, 31, 21, 0), null);
        // Assigned after the year (another territory: one open assignment per territory): belongs to 2027.
        var other = await fixture.SeedTerritoryAsync(giano.Id, 2);
        await fixture.SeedHistoryAsync(other.Id, rossi.Id, Utc(2026, 8, 31, 22, 30), null);

        var file = (await service.ExportAsync(new S13ExportRequest(2026, Rome, "it"), 1, Ct)).Value!;

        Assert.Equal(("S-13-2026-it.docx", CapturingWriter.Type), (file.FileName, file.ContentType));
        Assert.Equal(CapturingWriter.Bytes, file.Content);
        var row = Assert.Single(writer.Report!.Municipalities).Territories[0];
        Assert.Equal(
            [
                new S13Assignment("Mario Rossi", new DateOnly(2025, 9, 1), new DateOnly(2025, 11, 1)),
                new S13Assignment("Mario Rossi", new DateOnly(2026, 8, 31), null)
            ],
            row.Assignments);
        Assert.Equal(new DateOnly(2025, 10, 5), row.LastReturnedBefore);
    }

    [Fact]
    public async Task Report_ListsEveryMunicipalityAndTerritoryInOrder()
    {
        var montecchio = await fixture.SeedMunicipalityAsync("Montecchio");
        var giano = await fixture.SeedMunicipalityAsync("Giano");
        await fixture.SeedTerritoryAsync(montecchio.Id, 1);
        await fixture.SeedTerritoryAsync(giano.Id, 2);
        await fixture.SeedTerritoryAsync(giano.Id, 1);

        await service.ExportAsync(new S13ExportRequest(2026, null, "xx"), 1, Ct);

        var report = writer.Report!;
        Assert.Equal(("en", 2026), (report.Language, report.ServiceYear));
        Assert.Equal(
            ["Giano:1,2", "Montecchio:1"],
            report.Municipalities.Select(m => $"{m.Name}:{string.Join(',', m.Territories.Select(t => t.Number))}"));
        Assert.All(report.Municipalities.SelectMany(m => m.Territories), t => Assert.Empty(t.Assignments));
    }

    public static TheoryData<int?, string> InvalidYears => new()
    {
        { null, TextRules.Required },
        { S13ExportService.MinServiceYear - 1, TextRules.OutOfRange },
        { S13ExportService.MaxServiceYear + 1, TextRules.OutOfRange }
    };

    [Theory]
    [MemberData(nameof(InvalidYears))]
    public async Task InvalidServiceYear_ReturnsFieldError(int? year, string code)
    {
        var error = (await service.ExportAsync(new S13ExportRequest(year, null, null), 1, Ct)).Error!;

        Assert.Equal(ErrorKind.Validation, error.Kind);
        Assert.Equal([code], error.Fields![S13ExportService.ServiceYearField]);
        Assert.Null(writer.Report);
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour = 9, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private sealed class CapturingWriter : IS13Writer
    {
        public const string Type = "application/x-test";
        public static readonly byte[] Bytes = [1, 2, 3];

        public S13Report? Report { get; private set; }

        public string ContentType => Type;

        public void Write(S13Report report, Stream output)
        {
            Report = report;
            output.Write(Bytes);
        }
    }
}
