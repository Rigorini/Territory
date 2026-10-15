using Microsoft.Extensions.Logging.Abstractions;
using Territory.Application.Common;
using Territory.Application.Municipalities;
using Territory.Application.Domain;
using Territory.Infrastructure.Persistence;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class MunicipalityServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const int ActorId = 1;
    private TerritoryDbContext db = null!;
    private MunicipalityService service = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        db = fixture.CreateDbContext();
        service = new MunicipalityService(db, NullLogger<MunicipalityService>.Instance);
    }

    public ValueTask DisposeAsync() => db.DisposeAsync();

    [Fact]
    public async Task Create_TrimsName_AndListsWithTerritoryCount()
    {
        var created = await service.CreateAsync(new SaveMunicipalityRequest("  Montefalco "), ActorId, Ct);
        var giano = await fixture.SeedMunicipalityAsync("Giano dell'Umbria");
        await fixture.SeedTerritoryAsync(giano.Id, 1);
        await fixture.SeedTerritoryAsync(giano.Id, 2);

        Assert.Equal("Montefalco", created.Value!.Name);
        Assert.Equal(
            [new MunicipalityDto(giano.Id, "Giano dell'Umbria", 2), new MunicipalityDto(created.Value.Id, "Montefalco", 0)],
            await service.ListAsync(Ct));
    }

    [Fact]
    public async Task Create_WithInvalidName_ReturnsValidation()
    {
        var result = await service.CreateAsync(new SaveMunicipalityRequest(" "), ActorId, Ct);

        Assert.Equal(["required"], result.Error!.Fields![MunicipalityService.NameField]);
    }

    [Fact]
    public async Task Create_WithDuplicateNameDifferentCase_ReturnsConflict()
    {
        await fixture.SeedMunicipalityAsync("Bastardo");

        var result = await service.CreateAsync(new SaveMunicipalityRequest("BASTARDO"), ActorId, Ct);

        Assert.Equal(MunicipalityService.NameTaken, result.Error!.Code);
    }

    [Fact]
    public async Task Update_RenamesAndReturnsTerritoryCount()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Old");
        await fixture.SeedTerritoryAsync(municipality.Id, 1);

        var result = await service.UpdateAsync(municipality.Id, new SaveMunicipalityRequest("New"), ActorId, Ct);

        Assert.Equal(new MunicipalityDto(municipality.Id, "New", 1), result.Value);
        Assert.Equal("New", (await fixture.FindAsync<Municipality>(municipality.Id))!.Name);
    }

    [Fact]
    public async Task Update_KeepingOwnNameWithDifferentCase_Succeeds()
    {
        var municipality = await fixture.SeedMunicipalityAsync("gualdo");

        var result = await service.UpdateAsync(municipality.Id, new SaveMunicipalityRequest("Gualdo"), ActorId, Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Update_Errors()
    {
        await fixture.SeedMunicipalityAsync("First");
        var second = await fixture.SeedMunicipalityAsync("Second");

        Assert.Equal(ErrorKind.Validation, (await service.UpdateAsync(second.Id, new SaveMunicipalityRequest(null), ActorId, Ct)).Error!.Kind);
        Assert.Equal(MunicipalityService.NotFoundCode, (await service.UpdateAsync(99, new SaveMunicipalityRequest("X"), ActorId, Ct)).Error!.Code);
        Assert.Equal(MunicipalityService.NameTaken, (await service.UpdateAsync(second.Id, new SaveMunicipalityRequest("first"), ActorId, Ct)).Error!.Code);
    }

    [Fact]
    public async Task Delete_WithTerritories_IsRejected()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        await fixture.SeedTerritoryAsync(municipality.Id, 1);

        var result = await service.DeleteAsync(municipality.Id, ActorId, Ct);

        Assert.Equal(MunicipalityService.HasTerritories, result.Error!.Code);
        Assert.Equal(ErrorKind.BusinessRule, result.Error.Kind);
        Assert.NotNull(await fixture.FindAsync<Municipality>(municipality.Id));
    }

    [Fact]
    public async Task Delete_Empty_RemovesIt_ThenNotFound()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Empty");

        Assert.True((await service.DeleteAsync(municipality.Id, ActorId, Ct)).IsSuccess);
        Assert.Null(await fixture.FindAsync<Municipality>(municipality.Id));
        Assert.Equal(MunicipalityService.NotFoundCode, (await service.DeleteAsync(municipality.Id, ActorId, Ct)).Error!.Code);
    }
}
