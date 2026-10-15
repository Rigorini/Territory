using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Publishers;
using Territory.Infrastructure.Persistence;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class PublisherServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const int ActorId = 1;
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero));
    private TerritoryDbContext db = null!;
    private PublisherService service = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        db = fixture.CreateDbContext();
        service = new PublisherService(db, clock, NullLogger<PublisherService>.Instance, TestPersonalData.Protector);
    }

    public ValueTask DisposeAsync() => db.DisposeAsync();

    [Fact]
    public async Task Create_TrimsNames_AndListsOnlyActiveSortedByLastName()
    {
        var created = await service.CreateAsync(new SavePublisherRequest(" Mario ", " Rossi "), ActorId, Ct);
        await fixture.SeedPublisherAsync("Anna", "Bianchi");
        await fixture.SeedPublisherAsync("Gone", "Away", deleted: true);

        Assert.False(created.Value!.Reactivated);
        Assert.Equal(new PublisherDto(created.Value.Publisher.Id, "Mario", "Rossi"), created.Value.Publisher);
        Assert.Equal(["Bianchi", "Rossi"], (await service.ListAsync(Ct)).Select(p => p.LastName));
    }

    [Fact]
    public async Task Create_WithInvalidNames_ReturnsFieldCodes()
    {
        var result = await service.CreateAsync(new SavePublisherRequest("", new string('x', Publisher.NameMaxLength + 1)), ActorId, Ct);

        Assert.Equal(["required"], result.Error!.Fields![PublisherService.FirstNameField]);
        Assert.Equal(["maxLength"], result.Error.Fields[PublisherService.LastNameField]);
    }

    [Fact]
    public async Task Create_ActiveDuplicateDifferentCase_ReturnsConflict()
    {
        await fixture.SeedPublisherAsync("Mario", "Rossi");

        var result = await service.CreateAsync(new SavePublisherRequest("MARIO", "rossi"), ActorId, Ct);

        Assert.Equal(PublisherService.Duplicate, result.Error!.Code);
    }

    [Fact]
    public async Task Create_DeletedDuplicate_ReactivatesSameRecord()
    {
        var deleted = await fixture.SeedPublisherAsync("mario", "rossi", deleted: true);

        var result = await service.CreateAsync(new SavePublisherRequest("Mario", "Rossi"), ActorId, Ct);

        Assert.True(result.Value!.Reactivated);
        Assert.Equal(deleted.Id, result.Value.Publisher.Id);
        var stored = (await fixture.FindAsync<Publisher>(deleted.Id))!;
        Assert.False(stored.IsDeleted);
        Assert.Null(stored.DeletedAt);
        Assert.Equal(("Mario", "Rossi"), (stored.FirstName, stored.LastName));
    }

    [Fact]
    public async Task Update_Renames()
    {
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");

        var result = await service.UpdateAsync(publisher.Id, new SavePublisherRequest("Mario", "Verdi"), ActorId, Ct);

        Assert.Equal(new PublisherDto(publisher.Id, "Mario", "Verdi"), result.Value);
    }

    [Fact]
    public async Task Update_Errors()
    {
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        await fixture.SeedPublisherAsync("Anna", "Bianchi");
        await fixture.SeedPublisherAsync("Luca", "Verdi", deleted: true);
        var deleted = await fixture.SeedPublisherAsync("Old", "One", deleted: true);

        Assert.Equal(ErrorKind.Validation, (await service.UpdateAsync(publisher.Id, new SavePublisherRequest(null, "X"), ActorId, Ct)).Error!.Kind);
        Assert.Equal(PublisherService.NotFoundCode, (await service.UpdateAsync(deleted.Id, new SavePublisherRequest("A", "B"), ActorId, Ct)).Error!.Code);
        Assert.Equal(PublisherService.Duplicate, (await service.UpdateAsync(publisher.Id, new SavePublisherRequest("anna", "bianchi"), ActorId, Ct)).Error!.Code);
        Assert.Equal(PublisherService.DuplicateDeleted, (await service.UpdateAsync(publisher.Id, new SavePublisherRequest("Luca", "Verdi"), ActorId, Ct)).Error!.Code);
    }

    [Fact]
    public async Task Delete_IsLogical_AndKeepsHistory()
    {
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        var territory = await fixture.SeedTerritoryAsync(municipality.Id, 1);
        var history = await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: false);

        Assert.True((await service.DeleteAsync(publisher.Id, ActorId, Ct)).IsSuccess);

        var stored = (await fixture.FindAsync<Publisher>(publisher.Id))!;
        Assert.True(stored.IsDeleted);
        Assert.Equal(clock.GetUtcNow(), stored.DeletedAt);
        Assert.NotNull(await fixture.FindAsync<Assignment>(history.Id));
        Assert.Empty(await service.ListAsync(Ct));
        Assert.Equal(PublisherService.NotFoundCode, (await service.DeleteAsync(publisher.Id, ActorId, Ct)).Error!.Code);
    }

    [Fact]
    public async Task Delete_WithOpenAssignment_IsRejected()
    {
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        var territory = await fixture.SeedTerritoryAsync(municipality.Id, 1);
        await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: true);

        var result = await service.DeleteAsync(publisher.Id, ActorId, Ct);

        Assert.Equal(PublisherService.HasOpenAssignment, result.Error!.Code);
        Assert.False((await fixture.FindAsync<Publisher>(publisher.Id))!.IsDeleted);
    }
}
