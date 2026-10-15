using System.Net;
using System.Net.Http.Json;
using Territory.Application.Assignments;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class AssignmentApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "Correct-Horse-1";
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
    [InlineData("/api/assignments")]
    [InlineData("/api/territories/1/assignments")]
    public async Task Endpoints_RequireSession(string url)
    {
        var response = await ApiTestClient.Create(factory).Http.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Assignment_Lifecycle()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        var territory = await fixture.SeedTerritoryAsync(municipality.Id, 1);
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        var client = ApiTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("admin", Password)).StatusCode);

        var created = await client.SendAsync(HttpMethod.Post, "/api/assignments", new AssignRequest(territory.Id, publisher.Id, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl!.NoStore);
        var link = (await created.Content.ReadFromJsonAsync<ShareLinkDto>(Ct))!;
        Assert.True(ShareToken.IsWellFormed(link.Token));

        var again = await client.SendAsync(HttpMethod.Post, "/api/assignments", new AssignRequest(territory.Id, publisher.Id, null));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(AssignmentService.AlreadyAssigned, await ApiTestClient.ReadCodeAsync(again));

        var status = (await client.Http.GetFromJsonAsync<TerritoryStatusDto[]>("/api/assignments", Ct))!.Single();
        Assert.Equal(link.AssignmentId, status.Assignment!.Id);

        var renewed = await client.SendAsync(HttpMethod.Post, $"/api/assignments/{link.AssignmentId}/link", null);
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        Assert.NotEqual(link.Token, (await renewed.Content.ReadFromJsonAsync<ShareLinkDto>(Ct))!.Token);

        var returned = await client.SendAsync(HttpMethod.Post, $"/api/assignments/{link.AssignmentId}/return", new ReturnRequest(null));
        Assert.Equal(HttpStatusCode.NoContent, returned.StatusCode);

        var history = (await client.Http.GetFromJsonAsync<AssignmentHistoryDto[]>($"/api/territories/{territory.Id}/assignments", Ct))!;
        Assert.Equal("admin", Assert.Single(history).ReturnedBy);

        var corrected = new UpdateDatesRequest(history[0].AssignedAt.AddDays(-2), history[0].ReturnedAt);
        var updated = await client.SendAsync(HttpMethod.Put, $"/api/assignments/{link.AssignmentId}", corrected);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        var invalid = await client.SendAsync(HttpMethod.Put, $"/api/assignments/{link.AssignmentId}", new UpdateDatesRequest(null, null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var anonymous = await ApiTestClient.Create(factory).Http.PutAsJsonAsync($"/api/assignments/{link.AssignmentId}", corrected, Ct);
        Assert.NotEqual(HttpStatusCode.NoContent, anonymous.StatusCode);

        var missing = await client.SendAsync(HttpMethod.Post, "/api/assignments/999/return", new ReturnRequest(null));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
