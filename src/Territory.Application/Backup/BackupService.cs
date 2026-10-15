using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Territory.Application.Abstractions;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Export;
using Territory.Application.Publishers;

namespace Territory.Application.Backup;

/// <summary>
/// Full backup of publishers, territories (with municipalities) and assignments, with names decrypted.
/// Restoring replaces all of that data in one transaction; admin accounts are never read or touched.
/// </summary>
public sealed partial class BackupService(
    ITerritoryDbContext db, IPersonalDataCipher protector, TimeProvider clock, ILogger<BackupService> logger)
{
    public const string ContentType = "application/octet-stream";
    public const string FileExtension = ".ter";
    public const string EntryName = "backup.json";
    /// <summary>Upper bound of the decompressed document, to stop zip bombs that fit in the compressed request limit.</summary>
    public const long MaxDocumentBytes = 200L * 1024 * 1024;
    public const string FileNamePrefix = "backup-territori-";
    public const string ConfirmRequired = "backup.confirmRequired";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ExportFile> ExportAsync(int actorId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var document = new BackupDocument(
            BackupValidator.Format,
            BackupValidator.CurrentVersion,
            now,
            await db.Municipalities.AsNoTracking().OrderBy(m => m.Id)
                .Select(m => new BackupMunicipality(m.Id, m.Name)).ToListAsync(cancellationToken),
            await db.Territories.AsNoTracking().OrderBy(t => t.Id)
                .Select(t => new BackupTerritory(t.Id, t.MunicipalityId, t.Number, t.Name, t.MyMapsId)).ToListAsync(cancellationToken),
            await db.Publishers.AsNoTracking().OrderBy(p => p.Id)
                .Select(p => new BackupPublisher(p.Id, p.FirstName, p.LastName, p.IsDeleted, p.DeletedAt)).ToListAsync(cancellationToken),
            await db.Assignments.AsNoTracking().OrderBy(a => a.Id)
                .Select(a => new BackupAssignment(
                    a.TerritoryId, a.PublisherId, a.AssignedAt, a.ReturnedAt, a.ReturnedBy, a.ReturnedByName, a.TokenHash))
                .ToListAsync(cancellationToken));

        LogExported(actorId, Summarize(document));
        var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return new ExportFile($"{FileNamePrefix}{stamp}{FileExtension}", ContentType, Pack(JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions)));
    }

    /// <param name="confirmedOverwrite">The caller must acknowledge that all existing data of the registry is deleted.</param>
    public async Task<Result<BackupImportSummary>> ImportAsync(
        Stream content, bool confirmedOverwrite, int actorId, CancellationToken cancellationToken)
    {
        if (!confirmedOverwrite)
        {
            return AppError.BusinessRule(ConfirmRequired);
        }

        var validation = BackupValidator.Validate(await ReadAsync(content, cancellationToken));
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        var document = validation.Value!;
        try
        {
            await db.ExecuteInTransactionAsync(token => ReplaceAllAsync(document, token), cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex) || db.IsForeignKeyViolation(ex))
        {
            LogRejected(actorId);
            return new ValidationErrors().Add(BackupValidator.FileField, BackupValidator.InvalidValues).ToError();
        }

        var summary = Summarize(document);
        LogImported(actorId, summary);
        return summary;
    }

    private static byte[] Pack(byte[] json)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry(EntryName, CompressionLevel.Optimal).Open();
            entry.Write(json);
        }

        return buffer.ToArray();
    }

    // A file that is not a zip with exactly the expected entry is treated like an unreadable document.
    private static async Task<BackupDocument?> ReadAsync(Stream content, CancellationToken cancellationToken)
    {
        try
        {
            // ZipArchive needs a seekable stream; the request body is already capped by the endpoint limit.
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
            if (zip.Entries.Count != 1 || zip.Entries[0].FullName != EntryName || zip.Entries[0].Length > MaxDocumentBytes)
            {
                return null;
            }

            await using var entry = zip.Entries[0].Open();
            return await JsonSerializer.DeserializeAsync<BackupDocument>(entry, JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private async Task ReplaceAllAsync(BackupDocument document, CancellationToken cancellationToken)
    {
        // Children first: assignments reference both territories and publishers.
        await db.Assignments.ExecuteDeleteAsync(cancellationToken);
        await db.Territories.ExecuteDeleteAsync(cancellationToken);
        await db.Publishers.ExecuteDeleteAsync(cancellationToken);
        await db.Municipalities.ExecuteDeleteAsync(cancellationToken);

        // Rows get fresh identity values: the ids of the file are only used to wire the entities together.
        var municipalities = document.Municipalities!.ToDictionary(m => m.Id!.Value, m => new Municipality { Name = m.Name! });
        var territories = document.Territories!.ToDictionary(t => t.Id!.Value, t => new TerritoryArea
        {
            Municipality = municipalities[t.MunicipalityId!.Value],
            Number = t.Number!.Value,
            Name = t.Name!,
            MyMapsId = t.MyMapsId!
        });
        var publishers = document.Publishers!.ToDictionary(p => p.Id!.Value, ToPublisher);

        db.Municipalities.AddRange(municipalities.Values);
        db.Territories.AddRange(territories.Values);
        db.Publishers.AddRange(publishers.Values);
        db.Assignments.AddRange(document.Assignments!.Select(a => new Assignment
        {
            Territory = territories[a.TerritoryId!.Value],
            Publisher = publishers[a.PublisherId!.Value],
            AssignedAt = a.AssignedAt!.Value,
            ReturnedAt = a.ReturnedAt,
            ReturnedBy = a.ReturnedBy,
            ReturnedByName = a.ReturnedByName,
            TokenHash = a.TokenHash
        }));
        await db.SaveChangesAsync(cancellationToken);
    }

    // The blind index depends on the key of this installation, so it is recomputed instead of being exported.
    private Publisher ToPublisher(BackupPublisher p) => new()
    {
        FirstName = p.FirstName!,
        LastName = p.LastName!,
        NameHash = protector.BlindIndex(PublisherService.NameIdentity(p.FirstName!, p.LastName!)),
        IsDeleted = p.IsDeleted == true,
        DeletedAt = p.DeletedAt
    };

    private static BackupImportSummary Summarize(BackupDocument document) =>
        new(document.Municipalities!.Count, document.Territories!.Count, document.Publishers!.Count, document.Assignments!.Count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup exported by {ActorId}: {Summary}")]
    private partial void LogExported(int actorId, BackupImportSummary summary);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backup restored by {ActorId}, all registry data replaced: {Summary}")]
    private partial void LogImported(int actorId, BackupImportSummary summary);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backup restore by {ActorId} rejected by the database constraints")]
    private partial void LogRejected(int actorId);
}
