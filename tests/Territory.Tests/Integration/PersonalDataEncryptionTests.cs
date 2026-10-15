using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Territory.Application.Admins;
using Territory.Application.Assignments;
using Territory.Application.Publishers;
using Territory.Infrastructure.Security;

namespace Territory.Tests.Integration;

/// <summary>Verifies what is physically stored: names must never be readable in the database.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PersonalDataEncryptionTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Publisher_NamesAreEncryptedAtRest_AndReadBackInClear()
    {
        await using var db = fixture.CreateDbContext();
        var service = new PublisherService(db, TimeProvider.System, NullLogger<PublisherService>.Instance, TestPersonalData.Protector);

        var created = await service.CreateAsync(new SavePublisherRequest("Mario", "Rossi"), 1, Ct);

        var row = await ReadRowAsync("SELECT first_name, last_name, name_hash FROM publisher WHERE id = @id", created.Value!.Publisher.Id);
        Assert.StartsWith(AesGcmPersonalDataProtector.Prefix, row[0]);
        Assert.StartsWith(AesGcmPersonalDataProtector.Prefix, row[1]);
        Assert.Matches("^[0-9a-f]{64}$", row[2]);
        Assert.DoesNotContain("Mario", string.Join('|', row), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Rossi", string.Join('|', row), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new PublisherDto(created.Value.Publisher.Id, "Mario", "Rossi"), (await service.ListAsync(Ct)).Single());
    }

    [Fact]
    public async Task Publisher_ListIsOrderedInMemoryByLastThenFirstName()
    {
        await fixture.SeedPublisherAsync("Zeno", "Bianchi");
        await fixture.SeedPublisherAsync("anna", "Bianchi");
        await fixture.SeedPublisherAsync("Luca", "Álvarez");
        await using var db = fixture.CreateDbContext();
        var service = new PublisherService(db, TimeProvider.System, NullLogger<PublisherService>.Instance, TestPersonalData.Protector);

        var names = (await service.ListAsync(Ct)).Select(p => $"{p.LastName} {p.FirstName}");

        Assert.Equal(["Álvarez Luca", "Bianchi anna", "Bianchi Zeno"], names);
    }

    [Fact]
    public async Task Publisher_UniquenessIsCaseInsensitive_WithoutStoringThePlainName()
    {
        await using var db = fixture.CreateDbContext();
        var service = new PublisherService(db, TimeProvider.System, NullLogger<PublisherService>.Instance, TestPersonalData.Protector);
        await service.CreateAsync(new SavePublisherRequest("Mario", "Rossi"), 1, Ct);

        var duplicate = await service.CreateAsync(new SavePublisherRequest("MARIO", "rossi"), 1, Ct);

        Assert.Equal(PublisherService.Duplicate, duplicate.Error!.Code);
    }

    [Fact]
    public async Task Admin_DisplayNameAndReturnedByName_AreEncryptedAtRest()
    {
        var admin = await fixture.SeedAdminAsync("mrossi", "Correct-Horse-1");
        var municipality = await fixture.SeedMunicipalityAsync("Giano");
        var territory = await fixture.SeedTerritoryAsync(municipality.Id, 1);
        var publisher = await fixture.SeedPublisherAsync("Anna", "Bianchi");
        var open = await fixture.SeedAssignmentAsync(territory.Id, publisher.Id, open: true);
        await using var db = fixture.CreateDbContext();
        var service = new AssignmentService(db, TimeProvider.System, NullLogger<AssignmentService>.Instance);

        await service.ReturnAsync(open.Id, new ReturnRequest(null), admin.Id, Ct);

        var adminRow = await ReadRowAsync("SELECT display_name FROM users WHERE id = @id", admin.Id);
        var returnedBy = await ReadRowAsync("SELECT returned_by_name FROM assignment WHERE id = @id", open.Id);
        Assert.StartsWith(AesGcmPersonalDataProtector.Prefix, adminRow[0]);
        Assert.StartsWith(AesGcmPersonalDataProtector.Prefix, returnedBy[0]);
        Assert.Equal("Admin mrossi", TestPersonalData.Protector.Unprotect(returnedBy[0]));
    }

    [Fact]
    public async Task Migration_EncryptsLegacyPlainTextRows_AndIsIdempotent()
    {
        await ExecuteAsync("INSERT INTO publisher (id, first_name, last_name, name_hash) OVERRIDING SYSTEM VALUE VALUES (1, 'Mario', 'Rossi', 'pending-1')");
        await ExecuteAsync("INSERT INTO users (id, username, display_name, password_hash) OVERRIDING SYSTEM VALUE VALUES (1, 'admin', 'Mario Rossi', 'x')");
        await ExecuteAsync("INSERT INTO municipality (id, name) OVERRIDING SYSTEM VALUE VALUES (1, 'Giano')");
        await ExecuteAsync("INSERT INTO territory (id, municipality_id, number, name, mymaps_id) OVERRIDING SYSTEM VALUE VALUES (1, 1, 1, 'T', 'map-id-0000000001')");
        await ExecuteAsync(
            "INSERT INTO assignment (id, territory_id, publisher_id, assigned_at, returned_at, returned_by, returned_by_name) " +
            "OVERRIDING SYSTEM VALUE VALUES (1, 1, 1, now() - interval '10 days', now(), 2, 'Mario Rossi')");

        var first = await PersonalDataMigration.RunAsync(fixture.ConnectionString, TestPersonalData.Protector, Ct);
        var second = await PersonalDataMigration.RunAsync(fixture.ConnectionString, TestPersonalData.Protector, Ct);

        Assert.Equal(new PersonalDataMigration.Result(1, 1, 1), first);
        Assert.Equal(new PersonalDataMigration.Result(0, 0, 0), second);
        await using var db = fixture.CreateDbContext();
        var service = new PublisherService(db, TimeProvider.System, NullLogger<PublisherService>.Instance, TestPersonalData.Protector);
        Assert.Equal(new PublisherDto(1, "Mario", "Rossi"), (await service.ListAsync(Ct)).Single());
        var duplicate = await service.CreateAsync(new SavePublisherRequest("mario", "ROSSI"), 1, Ct);
        Assert.Equal(PublisherService.Duplicate, duplicate.Error!.Code);
        Assert.Equal("Mario Rossi", (await db.AdminUsers.ToListAsync(Ct)).Single().DisplayName);
    }

    [Fact]
    public async Task Migration_RepairsMissingHashOfAlreadyEncryptedRows()
    {
        var protector = TestPersonalData.Protector;
        await ExecuteAsync(
            $"INSERT INTO publisher (id, first_name, last_name, name_hash) OVERRIDING SYSTEM VALUE VALUES (1, '{protector.Protect("Mario")}', '{protector.Protect("Rossi")}', 'pending-1')");

        var result = await PersonalDataMigration.RunAsync(fixture.ConnectionString, protector, Ct);

        Assert.Equal(1, result.Publishers);
        var hash = (await ReadRowAsync("SELECT name_hash FROM publisher WHERE id = @id", 1))[0];
        Assert.Equal(protector.BlindIndex(PublisherService.NameIdentity("Mario", "Rossi")), hash);
    }

    private async Task<string[]> ReadRowAsync(string sql, long id)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));
        return [.. Enumerable.Range(0, reader.FieldCount).Select(reader.GetString)];
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
