using Microsoft.EntityFrameworkCore;
using Npgsql;
using Territory.Application.Domain;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class SchemaScriptTests(PostgresFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SchemaScripts_CanRunAgain_KeepingData()
    {
        var admin = await fixture.SeedAdminAsync("admin", "Correct-Horse-1");

        foreach (var script in PostgresFixture.SchemaScripts)
        {
            await fixture.RunScriptAsync(script);
        }

        Assert.Equal("Admin admin", (await fixture.FindAdminAsync(admin.Id))!.DisplayName);
    }

    [Fact]
    public async Task ReturnedByName_IsAllowedOnlyForAdminReturns()
    {
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        var territory = await fixture.SeedTerritoryAsync(municipality.Id, 1);
        var publisher = await fixture.SeedPublisherAsync("Mario", "Rossi");
        var now = DateTimeOffset.UtcNow;
        var byPublisher = await fixture.SeedHistoryAsync(territory.Id, publisher.Id, now.AddDays(-5), now, ReturnedBy.Publisher);
        await using var db = fixture.CreateDbContext();

        var update = () => db.Assignments.Where(a => a.Id == byPublisher.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReturnedByName, "Admin"), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<PostgresException>(update);
        Assert.Equal("ck_assignment_returned_by_name", error.ConstraintName);
    }
}