using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Territory.Application.Backup;
using Territory.Application.Domain;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class BackupApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "Correct-Horse-1";
    private const string ExportUrl = "/api/backup/export";
    private const string ImportUrl = "/api/backup/import?confirmOverwrite=true";
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
    public async Task Endpoints_RequireSession()
    {
        var client = ApiTestClient.Create(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync(ExportUrl, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.PostAsync(ImportUrl, new StringContent("{}"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Export_ContainsDecryptedRegistry_AndNoAdmins()
    {
        await SeedRegistryAsync();
        var client = await LoggedInAsync();

        var response = await client.Http.GetAsync(ExportUrl, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("backup-territori-", response.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.EndsWith(".ter", response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var json = Unzip(await response.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("\"firstName\": \"Mario\"", json);
        Assert.Contains("\"returnedByName\": \"Anna Admin\"", json);
        Assert.DoesNotContain("Admin admin", json, StringComparison.Ordinal);
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_WithoutConfirmation_IsRejected_AndKeepsData()
    {
        await SeedRegistryAsync();
        var client = await LoggedInAsync();
        var backup = await ExportJsonAsync(client);

        var response = await PostAsync(client, "/api/backup/import", backup);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(BackupService.ConfirmRequired, await ApiTestClient.ReadCodeAsync(response));
        Assert.Single(await ListPublishersAsync());
    }

    [Fact]
    public async Task Import_ReplacesAllExistingData_WithTheBackup()
    {
        var shared = await SeedRegistryAsync();
        var client = await LoggedInAsync();
        var backup = await ExportJsonAsync(client);
        await SeedOtherDataAsync();

        var response = await PostAsync(client, ImportUrl, backup);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<BackupImportSummary>(Ct);
        Assert.Equal(new BackupImportSummary(1, 2, 2, 2), summary);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(["Giano"], db.Municipalities.Select(m => m.Name).ToArray());
        var publishers = db.Publishers.AsEnumerable().ToList();
        Assert.Equal(["Mario Rossi", "Old Timer"], publishers.Select(p => $"{p.FirstName} {p.LastName}").Order().ToArray());
        Assert.True(publishers.Single(p => p.LastName == "Timer").IsDeleted);
        var open = db.Assignments.Single(a => a.ReturnedAt == null);
        Assert.Equal(shared, open.TokenHash);
        var closed = db.Assignments.Single(a => a.ReturnedAt != null);
        Assert.Equal((ReturnedBy.Admin, "Anna Admin"), (closed.ReturnedBy, closed.ReturnedByName));
        Assert.Equal(1, db.AdminUsers.Count());
    }

    [Fact]
    public async Task Import_InvalidFile_IsRejected_AndKeepsData()
    {
        await SeedRegistryAsync();
        var client = await LoggedInAsync();

        foreach (var body in new[] { "not json", "{}", """{"format":"territory-backup","version":99}""" })
        {
            var response = await PostAsync(client, ImportUrl, body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Single(await ListPublishersAsync(), p => p.LastName == "Rossi");
        Assert.Equal(2, fixture.CreateDbContext().Publishers.Count());
    }

    [Fact]
    public async Task Import_NotAZipOrWrongEntry_IsRejected_AndKeepsData()
    {
        await SeedRegistryAsync();
        var client = await LoggedInAsync();
        var xsrf = client.Cookies.Container.GetCookies(ApiTestClient.BaseAddress)[Territory.Api.Security.AuthSetup.XsrfCookieName]?.Value;

        foreach (var bytes in new[] { Encoding.UTF8.GetBytes("{}"), Zip("{}", "other.json") })
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ImportUrl) { Content = new ByteArrayContent(bytes) };
            request.Headers.Add(Territory.Api.Security.AuthSetup.XsrfHeaderName, xsrf);
            var response = await client.Http.SendAsync(request, Ct);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Single(await ListPublishersAsync());
    }

    [Fact]
    public async Task Import_BrokenReferences_AreRejected_AndKeepData()
    {
        await SeedRegistryAsync();
        var client = await LoggedInAsync();
        var backup = (await ExportJsonAsync(client)).Replace("\"publisherId\": 1", "\"publisherId\": 999", StringComparison.Ordinal);

        var response = await PostAsync(client, ImportUrl, backup);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").GetProperty("file");
        Assert.Contains(BackupValidator.BrokenReference, errors.EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(2, fixture.CreateDbContext().Publishers.Count());
    }

    [Fact]
    public async Task Import_AcceptsFilesLargerThanTheDefaultRequestLimit()
    {
        var publishers = Enumerable.Range(1, 8000).Select(i => new
        {
            id = i, firstName = $"{i:D5}{new string('a', 50)}", lastName = $"{i:D5}{new string('b', 50)}", isDeleted = false
        });
        var backup = JsonSerializer.Serialize(new
        {
            format = BackupValidator.Format, version = BackupValidator.CurrentVersion,
            municipalities = Array.Empty<object>(), territories = Array.Empty<object>(), publishers, assignments = Array.Empty<object>()
        });
        Assert.True(backup.Length > 1024 * 1024);
        var client = await LoggedInAsync();

        var response = await PostAsync(client, ImportUrl, backup);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(8000, fixture.CreateDbContext().Publishers.Count());
    }

    private static Task<HttpResponseMessage> PostAsync(ApiTestClient client, string url, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(Zip(body)) };
        request.Headers.Add(Territory.Api.Security.AuthSetup.XsrfHeaderName,
            client.Cookies.Container.GetCookies(ApiTestClient.BaseAddress)[Territory.Api.Security.AuthSetup.XsrfCookieName]?.Value);
        return client.Http.SendAsync(request, Ct);
    }

    private static async Task<string> ExportJsonAsync(ApiTestClient client) =>
        Unzip(await (await client.Http.GetAsync(ExportUrl, Ct)).Content.ReadAsByteArrayAsync(Ct));

    private static byte[] Zip(string json, string entryName = BackupService.EntryName)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry(entryName).Open(), new UTF8Encoding(false));
            writer.Write(json);
        }

        return buffer.ToArray();
    }

    private static string Unzip(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = Assert.Single(zip.Entries);
        Assert.Equal(BackupService.EntryName, entry.FullName);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private async Task<List<Publisher>> ListPublishersAsync()
    {
        await using var db = fixture.CreateDbContext();
        return db.Publishers.AsEnumerable().Where(p => !p.IsDeleted).ToList();
    }

    /// <returns>The share token hash of the open assignment.</returns>
    private async Task<byte[]> SeedRegistryAsync()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        var first = await fixture.SeedTerritoryAsync(municipality.Id, 1);
        var second = await fixture.SeedTerritoryAsync(municipality.Id, 2, "map-id-0000000002");
        var mario = await fixture.SeedPublisherAsync("Mario", "Rossi");
        var old = await fixture.SeedPublisherAsync("Old", "Timer", deleted: true);
        await fixture.SeedHistoryAsync(first.Id, old.Id, DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddDays(-30));
        var open = await fixture.SeedHistoryAsync(second.Id, mario.Id, DateTimeOffset.UtcNow.AddDays(-5), null);
        await using var db = fixture.CreateDbContext();
        var closed = db.Assignments.Single(a => a.ReturnedAt != null);
        closed.ReturnedByName = "Anna Admin";
        await db.SaveChangesAsync(Ct);
        return open.TokenHash!;
    }

    private async Task SeedOtherDataAsync()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Other");
        var territory = await fixture.SeedTerritoryAsync(municipality.Id, 5, "map-id-0000000009");
        var publisher = await fixture.SeedPublisherAsync("Gone", "Soon");
        await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: true);
    }

    private async Task<ApiTestClient> LoggedInAsync()
    {
        var client = ApiTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("admin", Password)).StatusCode);
        return client;
    }
}
