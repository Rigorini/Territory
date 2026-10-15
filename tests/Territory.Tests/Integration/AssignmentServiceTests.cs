using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Territory.Application.Assignments;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Territories;
using Territory.Infrastructure.Persistence;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class AssignmentServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const int ActorId = 1;

    // Seeded assignments are relative to the real clock: keep "now" aligned with it (second precision).
    private readonly FakeTimeProvider clock = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    private TerritoryDbContext db = null!;
    private AssignmentService service = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        db = fixture.CreateDbContext();
        service = new AssignmentService(db, clock, NullLogger<AssignmentService>.Instance);
    }

    public ValueTask DisposeAsync() => db.DisposeAsync();

    [Fact]
    public async Task ListStatus_ShowsOpenAssignmentOrLastReturn_SortedByMunicipalityAndNumber()
    {
        var giano = await fixture.SeedMunicipalityAsync("Giano");
        var bastardo = await fixture.SeedMunicipalityAsync("Bastardo");
        var t2 = await fixture.SeedTerritoryAsync(giano.Id, 2);
        var t1 = await fixture.SeedTerritoryAsync(giano.Id, 1);
        var b1 = await fixture.SeedTerritoryAsync(bastardo.Id, 1);
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        var returned = await fixture.SeedAssignmentAsync(t1.Id, publisher.Id, open: false);
        var open = await fixture.SeedAssignmentAsync(t2.Id, publisher.Id, open: true);

        var list = await service.ListStatusAsync(Ct);

        Assert.Equal([b1.Id, t1.Id, t2.Id], list.Select(s => s.TerritoryId));
        Assert.Null(list[0].Assignment);
        Assert.Null(list[0].LastReturnedAt);
        Assert.Null(list[1].Assignment);
        Assert.Equal(returned.ReturnedAt!.Value.ToUnixTimeSeconds(), list[1].LastReturnedAt!.Value.ToUnixTimeSeconds());
        Assert.Equal(open.Id, list[2].Assignment!.Id);
        Assert.Equal("Rossi Mario", list[2].Assignment!.PublisherName);
        Assert.Equal(publisher.Id, list[2].Assignment!.PublisherId);
    }

    [Fact]
    public async Task ListStatus_CountsOnlyAssignmentsOfTheLastYear()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        var territory = await fixture.SeedTerritoryAsync(municipality.Id, 1);
        var never = await fixture.SeedTerritoryAsync(municipality.Id, 2);
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: false);
        await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: true);
        var tooOld = clock.GetUtcNow().AddDays(-AssignmentService.AverageWindowDays - 5);
        db.Assignments.Add(new Assignment
        {
            TerritoryId = territory.Id,
            PublisherId = publisher.Id,
            AssignedAt = tooOld,
            ReturnedAt = tooOld.AddDays(10),
            ReturnedBy = ReturnedBy.Admin
        });
        await db.SaveChangesAsync(Ct);

        var list = await service.ListStatusAsync(Ct);

        Assert.Equal(2, list.Single(s => s.TerritoryId == territory.Id).AssignmentsLastYear);
        Assert.Equal(0, list.Single(s => s.TerritoryId == never.Id).AssignmentsLastYear);
    }

    [Fact]
    public async Task Assign_StoresOnlyTokenHash_AndDefaultsDateToNow()
    {
        var (territory, publisher) = await SeedAsync();

        var result = await service.AssignAsync(new AssignRequest(territory.Id, publisher.Id, null), ActorId, Ct);

        var stored = (await fixture.FindAsync<Assignment>(result.Value!.AssignmentId))!;
        Assert.Equal(ShareToken.Hash(result.Value.Token), stored.TokenHash);
        Assert.Equal(clock.GetUtcNow(), stored.AssignedAt);
        Assert.Null(stored.ReturnedAt);
    }

    [Fact]
    public async Task Assign_WithMissingFields_ReturnsRequired()
    {
        var result = await service.AssignAsync(new AssignRequest(null, null, null), ActorId, Ct);

        Assert.Equal([TextRules.Required], result.Error!.Fields![AssignmentService.TerritoryField]);
        Assert.Equal([TextRules.Required], result.Error.Fields[AssignmentService.PublisherField]);
    }

    [Fact]
    public async Task Assign_FutureDate_ReturnsInFuture()
    {
        var (territory, publisher) = await SeedAsync();

        var result = await service.AssignAsync(new AssignRequest(territory.Id, publisher.Id, clock.GetUtcNow().AddDays(1)), ActorId, Ct);

        Assert.Equal([DateRules.InFuture], result.Error!.Fields![AssignmentService.AssignedAtField]);
    }

    [Fact]
    public async Task Assign_UnknownTerritoryOrDeletedPublisher_ReturnsNotFoundFields()
    {
        var deleted = await fixture.SeedPublisherAsync("Gone", "Away", deleted: true);

        var result = await service.AssignAsync(new AssignRequest(999, deleted.Id, null), ActorId, Ct);

        Assert.Equal([TextRules.NotFound], result.Error!.Fields![AssignmentService.TerritoryField]);
        Assert.Equal([TextRules.NotFound], result.Error.Fields[AssignmentService.PublisherField]);
    }

    [Fact]
    public async Task Assign_AlreadyAssignedTerritory_ReturnsConflict()
    {
        var (territory, publisher) = await SeedAsync();
        await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: true);

        var result = await service.AssignAsync(new AssignRequest(territory.Id, publisher.Id, null), ActorId, Ct);

        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Equal(AssignmentService.AlreadyAssigned, result.Error.Code);
    }

    [Fact]
    public async Task Assign_BeforeLastReturn_ReturnsFieldError()
    {
        var (territory, publisher) = await SeedAsync();
        var previous = await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: false);

        var result = await service.AssignAsync(
            new AssignRequest(territory.Id, publisher.Id, previous.ReturnedAt!.Value.AddDays(-1)), ActorId, Ct);

        Assert.Equal([AssignmentService.BeforeLastReturn], result.Error!.Fields![AssignmentService.AssignedAtField]);
    }

    [Fact]
    public async Task Assign_ConcurrentOpenAssignment_IsSettledByUniqueIndex()
    {
        var (territory, publisher) = await SeedAsync();
        await using var other = fixture.CreateDbContext();
        var competitor = new AssignmentService(other, clock, NullLogger<AssignmentService>.Instance);

        var results = await Task.WhenAll(
            service.AssignAsync(new AssignRequest(territory.Id, publisher.Id, null), ActorId, Ct),
            competitor.AssignAsync(new AssignRequest(territory.Id, publisher.Id, null), ActorId, Ct));

        Assert.Single(results, r => r.IsSuccess);
        Assert.Equal(AssignmentService.AlreadyAssigned, Assert.Single(results, r => !r.IsSuccess).Error!.Code);
    }

    [Fact]
    public async Task RenewLink_ReplacesHash()
    {
        var (territory, publisher) = await SeedAsync();
        var first = (await service.AssignAsync(new AssignRequest(territory.Id, publisher.Id, null), ActorId, Ct)).Value!;

        var renewed = (await service.RenewLinkAsync(first.AssignmentId, ActorId, Ct)).Value!;

        Assert.NotEqual(first.Token, renewed.Token);
        var stored = (await fixture.FindAsync<Assignment>(first.AssignmentId))!;
        Assert.Equal(ShareToken.Hash(renewed.Token), stored.TokenHash);
    }

    [Fact]
    public async Task RenewLink_ClosedOrMissing_ReturnsErrors()
    {
        var (territory, publisher) = await SeedAsync();
        var closed = await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: false);

        Assert.Equal(AssignmentService.AlreadyReturned, (await service.RenewLinkAsync(closed.Id, ActorId, Ct)).Error!.Code);
        Assert.Equal(AssignmentService.NotFoundCode, (await service.RenewLinkAsync(999, ActorId, Ct)).Error!.Code);
    }

    [Fact]
    public async Task Return_ClosesAssignment_AndDeletesToken()
    {
        var (territory, publisher) = await SeedAsync();
        var open = await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: true);
        var returnedAt = clock.GetUtcNow().AddDays(-1);

        var result = await service.ReturnAsync(open.Id, new ReturnRequest(returnedAt), ActorId, Ct);

        Assert.True(result.IsSuccess);
        var stored = (await fixture.FindAsync<Assignment>(open.Id))!;
        Assert.Equal(returnedAt, stored.ReturnedAt);
        Assert.Equal(ReturnedBy.Admin, stored.ReturnedBy);
        Assert.Null(stored.ReturnedByName);
        Assert.Null(stored.TokenHash);
    }

    [Fact]
    public async Task Return_RecordsAdminName_KeptAfterRenameAndShownInHistory()
    {
        var (territory, publisher) = await SeedAsync();
        var admin = await fixture.SeedAdminAsync("mrossi", "Correct-Horse-1");
        var open = await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: true);

        var result = await service.ReturnAsync(open.Id, new ReturnRequest(null), admin.Id, Ct);
        await db.AdminUsers.Where(u => u.Id == admin.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, "Renamed"), Ct);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single((await service.HistoryAsync(territory.Id, Ct)).Value!);
        Assert.Equal("admin", entry.ReturnedBy);
        Assert.Equal("Admin mrossi", entry.ReturnedByName);
    }

    [Fact]
    public async Task Return_BeforeAssignedDate_ReturnsFieldError()
    {
        var (territory, publisher) = await SeedAsync();
        var open = await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: true);

        var result = await service.ReturnAsync(open.Id, new ReturnRequest(open.AssignedAt.AddDays(-1)), ActorId, Ct);

        Assert.Equal([AssignmentService.BeforeAssigned], result.Error!.Fields![AssignmentService.ReturnedAtField]);
    }

    [Fact]
    public async Task Return_InvalidDate_ClosedOrMissing_ReturnErrors()
    {
        var (territory, publisher) = await SeedAsync();
        var closed = await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: false);

        var future = await service.ReturnAsync(closed.Id, new ReturnRequest(clock.GetUtcNow().AddDays(2)), ActorId, Ct);
        Assert.Equal([DateRules.InFuture], future.Error!.Fields![AssignmentService.ReturnedAtField]);
        Assert.Equal(AssignmentService.AlreadyReturned, (await service.ReturnAsync(closed.Id, new ReturnRequest(null), ActorId, Ct)).Error!.Code);
        Assert.Equal(AssignmentService.NotFoundCode, (await service.ReturnAsync(999, new ReturnRequest(null), ActorId, Ct)).Error!.Code);
    }

    [Fact]
    public async Task History_ListsNewestFirst_WithReturnedBy()
    {
        var (territory, publisher) = await SeedAsync();
        var old = await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: false);
        var current = (await service.AssignAsync(new AssignRequest(territory.Id, publisher.Id, null), ActorId, Ct)).Value!;

        var history = (await service.HistoryAsync(territory.Id, Ct)).Value!;

        Assert.Equal([current.AssignmentId, old.Id], history.Select(h => h.Id));
        Assert.Null(history[0].ReturnedBy);
        Assert.Equal("admin", history[1].ReturnedBy);
        Assert.Equal("Rossi Mario", history[1].PublisherName);
    }

    [Fact]
    public async Task History_UnknownTerritory_ReturnsNotFound()
    {
        var result = await service.HistoryAsync(999, Ct);

        Assert.Equal(TerritoryService.NotFoundCode, result.Error!.Code);
    }

    [Fact]
    public async Task UpdateDates_CorrectsReturnedAssignment_KeepingStateAndToken()
    {
        var (territory, publisher) = await SeedAsync();
        var now = clock.GetUtcNow();
        var returned = await fixture.SeedHistoryAsync(territory.Id, publisher.Id, now.AddDays(-20), now.AddDays(-10), ReturnedBy.Publisher);

        var result = await service.UpdateDatesAsync(returned.Id, new UpdateDatesRequest(now.AddDays(-25), now.AddDays(-5)), ActorId, Ct);

        Assert.True(result.IsSuccess);
        var stored = (await fixture.FindAsync<Assignment>(returned.Id))!;
        Assert.Equal(now.AddDays(-25), stored.AssignedAt);
        Assert.Equal(now.AddDays(-5), stored.ReturnedAt);
        Assert.Equal(ReturnedBy.Publisher, stored.ReturnedBy);
    }

    [Fact]
    public async Task UpdateDates_OpenAssignment_ChangesOnlyAssignedDate()
    {
        var (territory, publisher) = await SeedAsync();
        var open = await fixture.SeedHistoryAsync(territory.Id, publisher.Id, clock.GetUtcNow().AddDays(-3), null);

        var result = await service.UpdateDatesAsync(open.Id, new UpdateDatesRequest(clock.GetUtcNow().AddDays(-7), null), ActorId, Ct);

        Assert.True(result.IsSuccess);
        var stored = (await fixture.FindAsync<Assignment>(open.Id))!;
        Assert.Equal(clock.GetUtcNow().AddDays(-7), stored.AssignedAt);
        Assert.Null(stored.ReturnedAt);
        Assert.Equal(open.TokenHash, stored.TokenHash);
    }

    [Fact]
    public async Task UpdateDates_AllowsTouchingNeighbours()
    {
        var (territory, publisher) = await SeedAsync();
        var now = clock.GetUtcNow();
        await fixture.SeedHistoryAsync(territory.Id, publisher.Id, now.AddDays(-30), now.AddDays(-20));
        var middle = await fixture.SeedHistoryAsync(territory.Id, publisher.Id, now.AddDays(-18), now.AddDays(-12));
        await fixture.SeedHistoryAsync(territory.Id, publisher.Id, now.AddDays(-10), null);

        var result = await service.UpdateDatesAsync(middle.Id, new UpdateDatesRequest(now.AddDays(-20), now.AddDays(-10)), ActorId, Ct);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(-22, -12, AssignmentService.AssignedAtField, AssignmentService.BeforeLastReturn)]
    [InlineData(-18, -8, AssignmentService.ReturnedAtField, AssignmentService.AfterNextAssignment)]
    [InlineData(-35, -5, AssignmentService.ReturnedAtField, AssignmentService.AfterNextAssignment)]
    public async Task UpdateDates_OverlappingNeighbours_ReturnsFieldError(int assignedDays, int returnedDays, string field, string code)
    {
        var (territory, publisher) = await SeedAsync();
        var now = clock.GetUtcNow();
        await fixture.SeedHistoryAsync(territory.Id, publisher.Id, now.AddDays(-30), now.AddDays(-20));
        var middle = await fixture.SeedHistoryAsync(territory.Id, publisher.Id, now.AddDays(-18), now.AddDays(-12));
        await fixture.SeedHistoryAsync(territory.Id, publisher.Id, now.AddDays(-10), null);

        var result = await service.UpdateDatesAsync(
            middle.Id, new UpdateDatesRequest(now.AddDays(assignedDays), now.AddDays(returnedDays)), ActorId, Ct);

        Assert.Contains(code, result.Error!.Fields![field]);
        Assert.Equal(now.AddDays(-18), (await fixture.FindAsync<Assignment>(middle.Id))!.AssignedAt);
    }

    [Fact]
    public async Task UpdateDates_OpenAssignmentBeforeLastReturn_ReturnsFieldError()
    {
        var (territory, publisher) = await SeedAsync();
        var now = clock.GetUtcNow();
        await fixture.SeedHistoryAsync(territory.Id, publisher.Id, now.AddDays(-30), now.AddDays(-20));
        var open = await fixture.SeedHistoryAsync(territory.Id, publisher.Id, now.AddDays(-10), null);

        var result = await service.UpdateDatesAsync(open.Id, new UpdateDatesRequest(now.AddDays(-40), null), ActorId, Ct);

        Assert.Equal([AssignmentService.BeforeLastReturn], result.Error!.Fields![AssignmentService.AssignedAtField]);
        Assert.False(result.Error.Fields!.ContainsKey(AssignmentService.ReturnedAtField));
    }

    [Fact]
    public async Task UpdateDates_InvalidValues_ReturnFieldErrors()
    {
        var (territory, publisher) = await SeedAsync();
        var now = clock.GetUtcNow();
        var returned = await fixture.SeedHistoryAsync(territory.Id, publisher.Id, now.AddDays(-20), now.AddDays(-10));
        var open = await fixture.SeedHistoryAsync(territory.Id, publisher.Id, now.AddDays(-5), null);

        var missing = (await service.UpdateDatesAsync(returned.Id, new UpdateDatesRequest(null, null), ActorId, Ct)).Error!;
        var reopened = (await service.UpdateDatesAsync(open.Id, new UpdateDatesRequest(now.AddDays(-5), now), ActorId, Ct)).Error!;
        var reversed = (await service.UpdateDatesAsync(returned.Id, new UpdateDatesRequest(now.AddDays(-10), now.AddDays(-20)), ActorId, Ct)).Error!;
        var future = (await service.UpdateDatesAsync(returned.Id, new UpdateDatesRequest(now.AddDays(-20), now.AddDays(2)), ActorId, Ct)).Error!;
        var tooOld = (await service.UpdateDatesAsync(returned.Id, new UpdateDatesRequest(DateRules.MinDate.AddDays(-1), now.AddDays(-10)), ActorId, Ct)).Error!;

        Assert.Equal([TextRules.Required], missing.Fields![AssignmentService.AssignedAtField]);
        Assert.Equal([TextRules.Required], missing.Fields![AssignmentService.ReturnedAtField]);
        Assert.Equal([TextRules.InvalidFormat], reopened.Fields![AssignmentService.ReturnedAtField]);
        Assert.Equal([AssignmentService.BeforeAssigned], reversed.Fields![AssignmentService.ReturnedAtField]);
        Assert.Contains(DateRules.InFuture, future.Fields![AssignmentService.ReturnedAtField]);
        Assert.Equal([TextRules.OutOfRange], tooOld.Fields![AssignmentService.AssignedAtField]);
    }

    [Fact]
    public async Task UpdateDates_UnknownAssignment_ReturnsNotFound()
    {
        var result = await service.UpdateDatesAsync(999, new UpdateDatesRequest(clock.GetUtcNow(), null), ActorId, Ct);

        Assert.Equal(AssignmentService.NotFoundCode, result.Error!.Code);
    }

    private async Task<(TerritoryArea Territory, Publisher Publisher)> SeedAsync()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        var territory = await fixture.SeedTerritoryAsync(municipality.Id, 1);
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        return (territory, publisher);
    }
}
