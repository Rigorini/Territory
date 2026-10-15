using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Export;
using Territory.Infrastructure.Persistence;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class RegisterExportServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Rome = "Europe/Rome";
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2025, 3, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly CapturingWriter writer = new();
    private TerritoryDbContext db = null!;
    private RegisterExportService service = null!;
    private Municipality montecchio = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        db = fixture.CreateDbContext();
        service = new RegisterExportService(db, [writer], clock, NullLogger<RegisterExportService>.Instance);
        await SeedAsync();
    }

    public ValueTask DisposeAsync() => db.DisposeAsync();

    [Fact]
    public async Task WholeHistory_ListsEveryTerritoryWithStatusAndLocalDays()
    {
        var file = (await service.ExportAsync(Request(), 1, Ct)).Value!;

        Assert.Equal(("registro-territori-2025-03-10.test", CapturingWriter.Type), (file.FileName, file.ContentType));
        Assert.Equal(CapturingWriter.Bytes, file.Content);
        var report = writer.Report!;
        Assert.Equal((new DateOnly(2025, 3, 10), (string?)null, "Registro dei territori"), (report.GeneratedOn, report.MunicipalityName, report.Texts.Title));
        Assert.Equal(["Giano 1", "Giano 2", "Montecchio 1"], report.Territories.Select(t => $"{t.MunicipalityName} {t.Number}"));

        var centro = report.Territories[0];
        Assert.Equal(("Rossi Mario", new DateOnly(2025, 1, 5)), (centro.CurrentPublisher, centro.Since));
        Assert.Equal(
            [
                new RegisterEntry("Bianchi Anna", new DateOnly(2024, 11, 1), new DateOnly(2025, 1, 1), ReturnedBy.Publisher),
                new RegisterEntry("Rossi Mario", new DateOnly(2025, 1, 5), null, null)
            ],
            centro.Entries);
        Assert.Equal((null, null, 0), (report.Territories[1].CurrentPublisher, report.Territories[1].Since, report.Territories[1].Entries.Count));
        Assert.Equal((null, new DateOnly(2024, 7, 1)), (report.Territories[2].CurrentPublisher, report.Territories[2].Since));
    }

    [Theory]
    [InlineData(Rome, "2025-01-01", "2025-12-31", "Giano 1:2|Giano 2:0|Montecchio 1:0")]
    [InlineData(null, "2025-01-01", null, "Giano 1:1|Giano 2:0|Montecchio 1:0")]
    [InlineData(Rome, null, "2024-12-31", "Giano 1:1|Giano 2:0|Montecchio 1:1")]
    [InlineData(Rome, "2024-06-15", "2024-06-20", "Giano 1:0|Giano 2:0|Montecchio 1:1")]
    public async Task Period_KeepsOverlappingAssignmentsInTheRequestedTimeZone(string? zone, string? from, string? to, string expected)
    {
        await service.ExportAsync(Request(from: from, to: to, zone: zone), 1, Ct);

        Assert.Equal(expected, string.Join('|', writer.Report!.Territories.Select(t => $"{t.MunicipalityName} {t.Number}:{t.Entries.Count}")));
    }

    [Fact]
    public async Task MunicipalityFilter_ExportsOnlyItsTerritories()
    {
        await service.ExportAsync(Request(municipalityId: montecchio.Id, language: "en"), 1, Ct);

        var report = writer.Report!;
        Assert.Equal(("Montecchio", "Territory register"), (report.MunicipalityName, report.Texts.Title));
        Assert.Equal("Montecchio", Assert.Single(report.Territories).MunicipalityName);
        Assert.Single(report.Territories[0].Entries);
    }

    public static TheoryData<RegisterExportRequest, string, string> InvalidRequests => new()
    {
        { Request() with { Format = null }, RegisterExportService.FormatField, TextRules.Required },
        { Request() with { Format = "pdf" }, RegisterExportService.FormatField, TextRules.InvalidFormat },
        { Request(from: "2025-13-01"), RegisterExportService.FromField, TextRules.InvalidFormat },
        { Request(from: "01/02/2025"), RegisterExportService.FromField, TextRules.InvalidFormat },
        { Request(from: "1999-12-31"), RegisterExportService.FromField, TextRules.OutOfRange },
        { Request(from: "2025-02-01", to: "2025-01-31"), RegisterExportService.ToField, TextRules.OutOfRange },
        { Request(municipalityId: 99999), RegisterExportService.MunicipalityField, TextRules.NotFound }
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequest_ReturnsFieldError(RegisterExportRequest request, string field, string code)
    {
        var error = (await service.ExportAsync(request, 1, Ct)).Error!;

        Assert.Equal(ErrorKind.Validation, error.Kind);
        Assert.Equal([code], error.Fields![field]);
        Assert.Null(writer.Report);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    [InlineData("Europe/Rome/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ResolveTimeZone_FallsBackToUtc(string? id) =>
        Assert.Equal(TimeZoneInfo.Utc, RegisterExportService.ResolveTimeZone(id));

    [Fact]
    public void ResolveTimeZone_FindsIanaZones() =>
        Assert.Equal(TimeSpan.FromHours(1), RegisterExportService.ResolveTimeZone(Rome).GetUtcOffset(new DateTime(2025, 1, 15)));

    [Fact]
    public void StartOfDay_SkipsMidnightInDaylightSavingGap()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 10),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 10, 1));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Gap", TimeSpan.Zero, "Gap", "Gap", "Gap DST", [rule]);

        Assert.Equal(new DateTimeOffset(2025, 3, 10, 0, 0, 0, TimeSpan.Zero), RegisterExportService.StartOfDay(new DateOnly(2025, 3, 10), zone));
        Assert.Equal(new DateTimeOffset(2025, 6, 30, 23, 0, 0, TimeSpan.Zero), RegisterExportService.StartOfDay(new DateOnly(2025, 7, 1), zone));
    }

    private static RegisterExportRequest Request(
        string? from = null, string? to = null, string? zone = Rome, int? municipalityId = null, string? language = null) =>
        new(CapturingWriter.Name, municipalityId, from, to, zone, language);

    private async Task SeedAsync()
    {
        var giano = await fixture.SeedMunicipalityAsync("Giano");
        montecchio = await fixture.SeedMunicipalityAsync("Montecchio");
        var centro = await fixture.SeedTerritoryAsync(giano.Id, 1);
        await fixture.SeedTerritoryAsync(giano.Id, 2);
        var borgo = await fixture.SeedTerritoryAsync(montecchio.Id, 1);
        var rossi = await fixture.SeedPublisherAsync("Mario", "Rossi");
        var bianchi = await fixture.SeedPublisherAsync("Anna", "Bianchi", deleted: true);

        // Returned at 00:30 of 1 January in Rome, still 31 December in UTC.
        await fixture.SeedHistoryAsync(centro.Id, bianchi.Id, Utc(2024, 11, 1, 10), Utc(2024, 12, 31, 23).AddMinutes(30), ReturnedBy.Publisher);
        await fixture.SeedHistoryAsync(centro.Id, rossi.Id, Utc(2025, 1, 5, 9), null);
        await fixture.SeedHistoryAsync(borgo.Id, bianchi.Id, Utc(2024, 6, 1, 9), Utc(2024, 7, 1, 9));
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private sealed class CapturingWriter : IRegisterWriter
    {
        public const string Name = "test";
        public const string Type = "application/x-test";
        public static readonly byte[] Bytes = [1, 2, 3];

        public RegisterReport? Report { get; private set; }

        public string Format => Name;

        public string ContentType => Type;

        public void Write(RegisterReport report, Stream output)
        {
            Report = report;
            output.Write(Bytes);
        }
    }
}
