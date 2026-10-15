using System.Net;
using ClosedXML.Excel;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class ExportApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "Correct-Horse-1";
    private const string Url = "/api/export/register";
    private DbApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        factory = new DbApiFactory(fixture.ConnectionString);
        await fixture.SeedAdminAsync("admin", Password);
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Export_RequiresSession()
    {
        var response = await ApiTestClient.Create(factory).Http.GetAsync($"{Url}?format=xlsx", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Xlsx_IsDownloadedAsUncachedAttachment()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        await fixture.SeedTerritoryAsync(municipality.Id, 7);
        var client = await LoggedInAsync();

        var response = await client.Http.GetAsync(
            $"{Url}?format=xlsx&municipalityId={municipality.Id}&from=2025-01-01&to=2025-12-31&timeZone=Europe%2FRome&language=en", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("registro-territori-", response.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.EndsWith(".xlsx", response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync(Ct));
        Assert.Equal(7, workbook.Worksheet("Status").Cell(2, 2).GetValue<int>());
    }

    [Fact]
    public async Task Docx_IsDownloaded()
    {
        var response = await (await LoggedInAsync()).Http.GetAsync($"{Url}?format=docx", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith(".docx", response.Content.Headers.ContentDisposition!.FileNameStar);
    }

    [Fact]
    public async Task S13_IsDownloadedAsDocx()
    {
        var response = await (await LoggedInAsync()).Http.GetAsync("/api/export/s13?serviceYear=2026&language=it", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("S-13-2026-it.docx", response.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task S13_RequiresServiceYear()
    {
        var response = await (await LoggedInAsync()).Http.GetAsync("/api/export/s13", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidQuery_ReturnsValidationProblem()
    {
        var response = await (await LoggedInAsync()).Http.GetAsync($"{Url}?format=pdf&from=yesterday", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<ApiTestClient> LoggedInAsync()
    {
        var client = ApiTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("admin", Password)).StatusCode);
        return client;
    }
}
