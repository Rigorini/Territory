using Territory.Application.Common;
using Territory.Application.Domain;

namespace Territory.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class PersistenceExtensionsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task TrySave_MapsConstraintViolations()
    {
        await fixture.SeedMunicipalityAsync("Giano");

        await using (var db = fixture.CreateDbContext())
        {
            db.Municipalities.Add(new Municipality { Name = "GIANO" });
            Assert.Equal(SaveOutcome.UniqueViolation, await db.TrySaveAsync(Ct));
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.Territories.Add(new TerritoryArea { MunicipalityId = 999, Number = 1, Name = "A", MyMapsId = "map-id-0000000001" });
            Assert.Equal(SaveOutcome.ForeignKeyViolation, await db.TrySaveAsync(Ct));
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.Municipalities.Add(new Municipality { Name = "Other" });
            Assert.Equal(SaveOutcome.Saved, await db.TrySaveAsync(Ct));
        }
    }
}
