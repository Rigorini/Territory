using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Territory.Api.Endpoints;
using Territory.Application.Maps;
using Territory.Application.Public;
using Territory.Tests.Application;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class PublicApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly FakeMapSource maps = new() { Kml = KmlSamples.Valid };
    private DbApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        factory = new DbApiFactory(fixture.ConnectionString, services => services.AddSingleton<IMapSource>(maps));
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task PublisherFlow_WorksWithoutSession_AndLinkDiesAfterReturn()
    {
        var token = await SeedAsync();
        var http = factory.CreateClient();

        var info = await SendAsync(http, HttpMethod.Get, "/api/public/territory", token);
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        Assert.True(info.Headers.CacheControl!.NoStore);
        Assert.Equal("Rossi Mario", (await info.Content.ReadFromJsonAsync<PublicTerritoryDto>(Ct))!.PublisherName);

        var map = await SendAsync(http, HttpMethod.Get, "/api/public/territory/map", token);
        Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        var json = await map.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("FeatureCollection", json.GetProperty("type").GetString());
        Assert.Equal("Polygon", json.GetProperty("features")[0].GetProperty("geometry").GetProperty("type").GetString());

        var returned = await SendAsync(http, HttpMethod.Post, "/api/public/territory/return", token);
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        Assert.NotNull(await returned.Content.ReadFromJsonAsync<ReturnReceiptDto>(Ct));

        var again = await SendAsync(http, HttpMethod.Post, "/api/public/territory/return", token);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(PublicTerritoryService.InvalidLink, await ApiTestClient.ReadCodeAsync(again));
    }

    [Fact]
    public async Task MissingOrDuplicatedHeader_IsInvalidLink()
    {
        var token = await SeedAsync();
        var http = factory.CreateClient();
        var duplicated = new HttpRequestMessage(HttpMethod.Get, "/api/public/territory");
        duplicated.Headers.Add(PublicEndpoints.TokenHeader, [token, token]);

        var missing = await http.GetAsync("/api/public/territory", Ct);
        var twice = await http.SendAsync(duplicated, Ct);

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(PublicTerritoryService.InvalidLink, await ApiTestClient.ReadCodeAsync(missing));
        Assert.Equal(HttpStatusCode.NotFound, twice.StatusCode);
    }

    [Fact]
    public async Task UnavailableMap_Returns503()
    {
        var token = await SeedAsync();
        maps.Kml = null;

        var response = await SendAsync(factory.CreateClient(), HttpMethod.Get, "/api/public/territory/map", token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(PublicTerritoryService.MapUnavailable, await ApiTestClient.ReadCodeAsync(response));
    }

    [Fact]
    public async Task PublicEndpoints_AreRateLimited()
    {
        var http = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 31; i++)
        {
            statuses.Add((await http.GetAsync("/api/public/territory", Ct)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.All(statuses[..^1], s => Assert.Equal(HttpStatusCode.NotFound, s));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(PublicEndpoints.TokenHeader, token);
        return http.SendAsync(request, Ct);
    }

    private async Task<string> SeedAsync()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        var territory = await fixture.SeedTerritoryAsync(municipality.Id, 1);
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        return (await fixture.SeedSharedAssignmentAsync(territory.Id, publisher.Id)).Token;
    }
}
