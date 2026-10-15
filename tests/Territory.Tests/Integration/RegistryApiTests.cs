using System.Net;
using System.Net.Http.Json;
using Territory.Application.Municipalities;
using Territory.Application.Publishers;
using Territory.Application.Territories;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class RegistryApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "Correct-Horse-1";
    private static readonly string MapUrl = MyMapsLink.ToViewerUrl("1AbCdEfGhIjKlMnOpQrStUvWxYz012345");
    private DbApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        factory = new DbApiFactory(fixture.ConnectionString);
        await fixture.SeedAdminAsync("admin", Password);
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Theory]
    [InlineData("/api/municipalities")]
    [InlineData("/api/territories")]
    [InlineData("/api/publishers")]
    public async Task Endpoints_RequireSession(string url)
    {
        var response = await ApiTestClient.Create(factory).Http.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mutations_WithoutAntiforgeryHeader_AreRejected()
    {
        var client = await LoggedInAsync();

        var response = await client.Http.PostAsJsonAsync("/api/municipalities", new SaveMunicipalityRequest("Giano"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MunicipalityAndTerritory_Lifecycle()
    {
        var client = await LoggedInAsync();

        var createdMunicipality = await client.SendAsync(HttpMethod.Post, "/api/municipalities", new SaveMunicipalityRequest("Giano"));
        Assert.Equal(HttpStatusCode.Created, createdMunicipality.StatusCode);
        var municipality = (await createdMunicipality.Content.ReadFromJsonAsync<MunicipalityDto>(Ct))!;
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(HttpMethod.Put, $"/api/municipalities/{municipality.Id}", new SaveMunicipalityRequest("Giano dell'Umbria"))).StatusCode);

        var createdTerritory = await client.SendAsync(HttpMethod.Post, "/api/territories", new SaveTerritoryRequest(municipality.Id, 1, "Centro", MapUrl));
        Assert.Equal(HttpStatusCode.Created, createdTerritory.StatusCode);
        var territory = (await createdTerritory.Content.ReadFromJsonAsync<TerritoryDto>(Ct))!;
        Assert.Equal("Giano dell'Umbria", territory.MunicipalityName);

        var duplicate = await client.SendAsync(HttpMethod.Post, "/api/territories", new SaveTerritoryRequest(municipality.Id, 1, "Altro", MapUrl));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(TerritoryService.NumberTaken, await ApiTestClient.ReadCodeAsync(duplicate));

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(HttpMethod.Put, $"/api/territories/{territory.Id}", new SaveTerritoryRequest(municipality.Id, 2, "Centro", MapUrl))).StatusCode);
        Assert.Single((await client.Http.GetFromJsonAsync<TerritoryDto[]>("/api/territories", Ct))!);
        Assert.Equal(1, (await client.Http.GetFromJsonAsync<MunicipalityDto[]>("/api/municipalities", Ct))!.Single().TerritoryCount);

        var blocked = await client.SendAsync(HttpMethod.Delete, $"/api/municipalities/{municipality.Id}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        Assert.Equal(MunicipalityService.HasTerritories, await ApiTestClient.ReadCodeAsync(blocked));

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Delete, $"/api/territories/{territory.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Delete, $"/api/municipalities/{municipality.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteTerritory_WithAssignments_NeedsConfirmationQuery()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        var territory = await fixture.SeedTerritoryAsync(municipality.Id, 1);
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: false);
        var client = await LoggedInAsync();

        var unconfirmed = await client.SendAsync(HttpMethod.Delete, $"/api/territories/{territory.Id}");
        Assert.Equal(HttpStatusCode.Conflict, unconfirmed.StatusCode);
        Assert.Equal(TerritoryService.HasAssignments, await ApiTestClient.ReadCodeAsync(unconfirmed));

        var confirmed = await client.SendAsync(HttpMethod.Delete, $"/api/territories/{territory.Id}?confirmAssignments=true");
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
    }

    [Fact]
    public async Task InvalidTerritory_ReturnsFieldCodes()
    {
        var client = await LoggedInAsync();

        var response = await client.SendAsync(HttpMethod.Post, "/api/territories", new SaveTerritoryRequest(null, 0, "<b>x</b>", "javascript:alert(1)"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
        Assert.Equal("invalidFormat", body.GetProperty("errors").GetProperty("mapUrl")[0].GetString());
        Assert.Equal("outOfRange", body.GetProperty("errors").GetProperty("number")[0].GetString());
    }

    [Fact]
    public async Task Publisher_Lifecycle_WithReactivation()
    {
        var client = await LoggedInAsync();

        var created = await client.SendAsync(HttpMethod.Post, "/api/publishers", new SavePublisherRequest("Mario", "Rossi"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var publisher = (await created.Content.ReadFromJsonAsync<CreatePublisherResult>(Ct))!.Publisher;

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(HttpMethod.Put, $"/api/publishers/{publisher.Id}", new SavePublisherRequest("Mario", "Rossi"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Delete, $"/api/publishers/{publisher.Id}")).StatusCode);
        Assert.Empty((await client.Http.GetFromJsonAsync<PublisherDto[]>("/api/publishers", Ct))!);

        var reactivated = await client.SendAsync(HttpMethod.Post, "/api/publishers", new SavePublisherRequest("mario", "rossi"));
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        var result = (await reactivated.Content.ReadFromJsonAsync<CreatePublisherResult>(Ct))!;
        Assert.True(result.Reactivated);
        Assert.Equal(publisher.Id, result.Publisher.Id);
    }

    private async Task<ApiTestClient> LoggedInAsync()
    {
        var client = ApiTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("admin", Password)).StatusCode);
        return client;
    }
}
