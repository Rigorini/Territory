using Microsoft.Extensions.Logging.Abstractions;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Territories;
using Territory.Infrastructure.Persistence;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class TerritoryServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const int ActorId = 1;
    private const string MapId = "1AbCdEfGhIjKlMnOpQrStUvWxYz012345";
    private const string OtherMapId = "9ZyXwVuTsRqPoNmLkJiHgFeDcBa98765";
    private static readonly string MapUrl = MyMapsLink.ToViewerUrl(MapId);
    private TerritoryDbContext db = null!;
    private TerritoryService service = null!;
    private Municipality giano = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        db = fixture.CreateDbContext();
        service = new TerritoryService(db, NullLogger<TerritoryService>.Instance);
        giano = await fixture.SeedMunicipalityAsync("Giano");
    }

    public ValueTask DisposeAsync() => db.DisposeAsync();

    [Fact]
    public async Task Create_StoresOnlyMapId_AndReturnsViewerUrl()
    {
        var result = await service.CreateAsync(
            new SaveTerritoryRequest(giano.Id, 3, " Centro ", $"https://www.google.com/maps/d/edit?mid={MapId}&usp=sharing"), ActorId, Ct);

        Assert.Equal(new TerritoryDto(result.Value!.Id, giano.Id, "Giano", 3, "Centro", MapUrl, 0), result.Value);
        Assert.Equal(MapId, (await fixture.FindAsync<TerritoryArea>(result.Value.Id))!.MyMapsId);
    }

    [Fact]
    public async Task List_IsOrderedByMunicipalityThenNumber_WithAssignmentCount()
    {
        var bastardo = await fixture.SeedMunicipalityAsync("Bastardo");
        var g2 = await fixture.SeedTerritoryAsync(giano.Id, 2);
        await fixture.SeedTerritoryAsync(giano.Id, 1);
        await fixture.SeedTerritoryAsync(bastardo.Id, 5);
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        await fixture.SeedAssignmentAsync(g2.Id, publisher.Id, open: false);

        var list = await service.ListAsync(Ct);

        Assert.Equal([("Bastardo", 5), ("Giano", 1), ("Giano", 2)], list.Select(t => (t.MunicipalityName, t.Number)));
        Assert.Equal(1, list.Single(t => t.Id == g2.Id).AssignmentCount);
    }

    [Fact]
    public async Task Create_WithMissingFields_ReturnsAllFieldCodes()
    {
        var result = await service.CreateAsync(new SaveTerritoryRequest(null, null, null, null), ActorId, Ct);

        var fields = result.Error!.Fields!;
        Assert.Equal(["required"], fields[TerritoryService.MunicipalityField]);
        Assert.Equal(["required"], fields[TerritoryService.NumberField]);
        Assert.Equal(["required"], fields[TerritoryService.NameField]);
        Assert.Equal(["required"], fields[TerritoryService.MapUrlField]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(TerritoryService.MaxNumber + 1)]
    public async Task Create_WithNumberOutOfRange_ReturnsValidation(int number)
    {
        var result = await service.CreateAsync(new SaveTerritoryRequest(giano.Id, number, "A", MapUrl), ActorId, Ct);

        Assert.Equal(["outOfRange"], result.Error!.Fields![TerritoryService.NumberField]);
    }

    [Fact]
    public async Task Create_WithNonMyMapsUrl_ReturnsInvalidFormat()
    {
        var result = await service.CreateAsync(new SaveTerritoryRequest(giano.Id, 1, "A", "https://evil.example/?mid=" + MapId), ActorId, Ct);

        Assert.Equal(["invalidFormat"], result.Error!.Fields![TerritoryService.MapUrlField]);
    }

    [Fact]
    public async Task Create_WithUnknownMunicipality_ReturnsNotFoundField()
    {
        var result = await service.CreateAsync(new SaveTerritoryRequest(999, 1, "A", MapUrl), ActorId, Ct);

        Assert.Equal(["notFound"], result.Error!.Fields![TerritoryService.MunicipalityField]);
    }

    [Fact]
    public async Task Create_WithNumberTakenInSameMunicipality_ReturnsConflict_ButOtherMunicipalityIsFine()
    {
        await fixture.SeedTerritoryAsync(giano.Id, 1);
        var other = await fixture.SeedMunicipalityAsync("Other");

        var duplicate = await service.CreateAsync(new SaveTerritoryRequest(giano.Id, 1, "A", MapUrl), ActorId, Ct);
        var elsewhere = await service.CreateAsync(new SaveTerritoryRequest(other.Id, 1, "A", MapUrl), ActorId, Ct);

        Assert.Equal(TerritoryService.NumberTaken, duplicate.Error!.Code);
        Assert.True(elsewhere.IsSuccess);
    }

    [Fact]
    public async Task Update_WithNewMap_ClearsCachedKml()
    {
        var territory = await fixture.SeedTerritoryAsync(giano.Id, 1, MapId);
        await using (var other = fixture.CreateDbContext())
        {
            var tracked = await other.Territories.FindAsync([territory.Id], Ct);
            tracked!.MapKml = "<kml/>";
            tracked.MapFetchedAt = DateTimeOffset.UtcNow;
            await other.SaveChangesAsync(Ct);
        }

        var sameMap = await service.UpdateAsync(territory.Id, new SaveTerritoryRequest(giano.Id, 7, "Renamed", MapUrl), ActorId, Ct);
        Assert.Equal("<kml/>", (await fixture.FindAsync<TerritoryArea>(territory.Id))!.MapKml);

        await service.UpdateAsync(territory.Id, new SaveTerritoryRequest(giano.Id, 7, "Renamed", MyMapsLink.ToViewerUrl(OtherMapId)), ActorId, Ct);
        var stored = (await fixture.FindAsync<TerritoryArea>(territory.Id))!;

        Assert.Equal((7, "Renamed"), (sameMap.Value!.Number, sameMap.Value.Name));
        Assert.Equal(OtherMapId, stored.MyMapsId);
        Assert.Null(stored.MapKml);
        Assert.Null(stored.MapFetchedAt);
    }

    [Fact]
    public async Task Update_Errors()
    {
        var first = await fixture.SeedTerritoryAsync(giano.Id, 1);
        await fixture.SeedTerritoryAsync(giano.Id, 2);

        Assert.Equal(TerritoryService.NotFoundCode, (await service.UpdateAsync(99, new SaveTerritoryRequest(giano.Id, 1, "A", MapUrl), ActorId, Ct)).Error!.Code);
        Assert.Equal(ErrorKind.Validation, (await service.UpdateAsync(first.Id, new SaveTerritoryRequest(giano.Id, 1, "", MapUrl), ActorId, Ct)).Error!.Kind);
        Assert.Equal(TerritoryService.NumberTaken, (await service.UpdateAsync(first.Id, new SaveTerritoryRequest(giano.Id, 2, "A", MapUrl), ActorId, Ct)).Error!.Code);
        Assert.True((await service.UpdateAsync(first.Id, new SaveTerritoryRequest(giano.Id, 1, "A", MapUrl), ActorId, Ct)).IsSuccess);
    }

    [Fact]
    public async Task Delete_WithAssignments_RequiresConfirmation_ThenCascades()
    {
        var territory = await fixture.SeedTerritoryAsync(giano.Id, 1);
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        var assignment = await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: true);

        var unconfirmed = await service.DeleteAsync(territory.Id, confirmAssignments: false, ActorId, Ct);
        Assert.Equal(TerritoryService.HasAssignments, unconfirmed.Error!.Code);
        Assert.Equal(ErrorKind.Conflict, unconfirmed.Error.Kind);
        Assert.NotNull(await fixture.FindAsync<TerritoryArea>(territory.Id));

        Assert.True((await service.DeleteAsync(territory.Id, confirmAssignments: true, ActorId, Ct)).IsSuccess);
        Assert.Null(await fixture.FindAsync<TerritoryArea>(territory.Id));
        Assert.Null(await fixture.FindAsync<Assignment>(assignment.Id));
    }

    [Fact]
    public async Task Delete_WithoutAssignments_NeedsNoConfirmation_ThenNotFound()
    {
        var territory = await fixture.SeedTerritoryAsync(giano.Id, 1);

        Assert.True((await service.DeleteAsync(territory.Id, confirmAssignments: false, ActorId, Ct)).IsSuccess);
        Assert.Equal(TerritoryService.NotFoundCode, (await service.DeleteAsync(territory.Id, false, ActorId, Ct)).Error!.Code);
    }
}
