using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Territory.Application.Assignments;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Public;
using Territory.Infrastructure.Persistence;
using Territory.Tests.Application;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class PublicTerritoryServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly FakeTimeProvider clock = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    private readonly FakeMapSource maps = new();
    private TerritoryDbContext db = null!;
    private PublicTerritoryService service = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        db = fixture.CreateDbContext();
        service = new PublicTerritoryService(db, maps, clock, NullLogger<PublicTerritoryService>.Instance);
    }

    public ValueTask DisposeAsync() => db.DisposeAsync();

    public static TheoryData<string?> InvalidTokens => new()
    {
        (string?)null,
        "",
        "too-short",
        new string('*', 43),
        ShareToken.Issue().Token
    };

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task UnknownOrMalformedTokens_AreAllInvalidLinks(string? token)
    {
        await SeedAsync();

        AssertInvalid((await service.GetAsync(token, Ct)).Error);
        AssertInvalid((await service.GetMapAsync(token, Ct)).Error);
        AssertInvalid((await service.ReturnAsync(token, Ct)).Error);
        Assert.Empty(maps.Requests);
    }

    [Fact]
    public async Task Get_ReturnsTerritoryAndPublisher()
    {
        var (territory, assignment, token) = await SeedAsync();

        var dto = (await service.GetAsync(token, Ct)).Value!;

        Assert.Equal(new PublicTerritoryDto("Giano", territory.Number, territory.Name, "Rossi Mario", assignment.AssignedAt), dto);
    }

    [Fact]
    public async Task Return_ClosesAssignmentAsPublisher_AndInvalidatesTheLink()
    {
        var (_, assignment, token) = await SeedAsync();

        var receipt = (await service.ReturnAsync(token, Ct)).Value!;

        Assert.Equal(new ReturnReceiptDto(assignment.AssignedAt, clock.GetUtcNow()), receipt);
        var stored = (await fixture.FindAsync<Assignment>(assignment.Id))!;
        Assert.Equal(clock.GetUtcNow(), stored.ReturnedAt);
        Assert.Equal(ReturnedBy.Publisher, stored.ReturnedBy);
        Assert.Null(stored.TokenHash);
        AssertInvalid((await service.ReturnAsync(token, Ct)).Error);
        AssertInvalid((await service.GetAsync(token, Ct)).Error);
    }

    [Fact]
    public async Task Return_NeverPrecedesTheAssignmentDate()
    {
        var (_, assignment, token) = await SeedAsync();
        var early = new FakeTimeProvider(assignment.AssignedAt.AddMinutes(-2));
        var skewed = new PublicTerritoryService(db, maps, early, NullLogger<PublicTerritoryService>.Instance);

        var receipt = (await skewed.ReturnAsync(token, Ct)).Value!;

        Assert.Equal(assignment.AssignedAt, receipt.ReturnedAt);
    }

    [Fact]
    public async Task Map_WithoutCache_DownloadsAndStoresIt()
    {
        var (territory, _, token) = await SeedAsync();
        maps.Kml = KmlSamples.Valid;

        var map = (await service.GetMapAsync(token, Ct)).Value!;

        Assert.Single(map.Features);
        Assert.Equal([territory.MyMapsId], maps.Requests);
        var stored = (await fixture.FindAsync<TerritoryArea>(territory.Id))!;
        Assert.Equal(KmlSamples.Valid, stored.MapKml);
        Assert.Equal(clock.GetUtcNow(), stored.MapFetchedAt);
    }

    [Fact]
    public async Task Map_WithFreshCache_DoesNotDownload()
    {
        var (territory, _, token) = await SeedAsync();
        await fixture.SetMapCacheAsync(territory.Id, KmlSamples.Valid, clock.GetUtcNow().AddMinutes(-59));

        var map = (await service.GetMapAsync(token, Ct)).Value!;

        Assert.Single(map.Features);
        Assert.Empty(maps.Requests);
    }

    [Fact]
    public async Task Map_WithStaleCache_RefreshesIt()
    {
        var (territory, _, token) = await SeedAsync();
        await fixture.SetMapCacheAsync(territory.Id, KmlSamples.Document(""), clock.GetUtcNow().AddHours(-2));
        maps.Kml = KmlSamples.Valid;

        var map = (await service.GetMapAsync(token, Ct)).Value!;

        Assert.Single(map.Features);
        Assert.Single(maps.Requests);
        Assert.Equal(KmlSamples.Valid, (await fixture.FindAsync<TerritoryArea>(territory.Id))!.MapKml);
    }

    [Fact]
    public async Task Map_WhenDownloadFails_FallsBackToStaleCache()
    {
        var (territory, _, token) = await SeedAsync();
        var fetchedAt = clock.GetUtcNow().AddDays(-3);
        await fixture.SetMapCacheAsync(territory.Id, KmlSamples.Valid, fetchedAt);
        maps.Kml = "<html>Sign in</html>";

        var map = (await service.GetMapAsync(token, Ct)).Value!;

        Assert.Single(map.Features);
        Assert.Equal(fetchedAt, (await fixture.FindAsync<TerritoryArea>(territory.Id))!.MapFetchedAt);
    }

    [Fact]
    public async Task Map_WithoutCacheAndDownload_IsUnavailable()
    {
        var (_, _, token) = await SeedAsync();

        var error = (await service.GetMapAsync(token, Ct)).Error!;

        Assert.Equal(ErrorKind.Unavailable, error.Kind);
        Assert.Equal(PublicTerritoryService.MapUnavailable, error.Code);
    }

    [Fact]
    public async Task Map_WithCorruptFreshCache_IsUnavailable()
    {
        var (territory, _, token) = await SeedAsync();
        await fixture.SetMapCacheAsync(territory.Id, "<broken", clock.GetUtcNow());

        var error = (await service.GetMapAsync(token, Ct)).Error!;

        Assert.Equal(PublicTerritoryService.MapUnavailable, error.Code);
        Assert.Empty(maps.Requests);
    }

    private static void AssertInvalid(AppError? error)
    {
        Assert.NotNull(error);
        Assert.Equal(ErrorKind.NotFound, error.Kind);
        Assert.Equal(PublicTerritoryService.InvalidLink, error.Code);
    }

    private async Task<(TerritoryArea Territory, Assignment Assignment, string Token)> SeedAsync()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        var territory = await fixture.SeedTerritoryAsync(municipality.Id, 7);
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        var (assignment, token) = await fixture.SeedSharedAssignmentAsync(territory.Id, publisher.Id);
        return (territory, assignment, token);
    }
}
