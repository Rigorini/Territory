using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Territory.Application.Abstractions;
using Territory.Application.Common;
using Territory.Application.Domain;

namespace Territory.Application.Territories;

/// <summary>MapUrl is the admin-facing MyMaps viewer link rebuilt from the stored map id.</summary>
public sealed record TerritoryDto(
    int Id, int MunicipalityId, string MunicipalityName, int Number, string Name, string MapUrl, int AssignmentCount);

public sealed record SaveTerritoryRequest(int? MunicipalityId, int? Number, string? Name, string? MapUrl);

public sealed partial class TerritoryService(ITerritoryDbContext db, ILogger<TerritoryService> logger)
{
    public const int MaxNumber = 9999;
    public const string MunicipalityField = "municipalityId";
    public const string NumberField = "number";
    public const string NameField = "name";
    public const string MapUrlField = "mapUrl";
    public const string NumberTaken = "territories.numberTaken";
    public const string NotFoundCode = "territories.notFound";
    public const string HasAssignments = "territories.hasAssignments";

    private sealed record ValidTerritory(int MunicipalityId, int Number, string Name, string MapId);

    public async Task<IReadOnlyList<TerritoryDto>> ListAsync(CancellationToken cancellationToken) =>
        await Project(db.Territories.AsNoTracking().OrderBy(t => t.Municipality!.Name).ThenBy(t => t.Number))
            .ToListAsync(cancellationToken);

    public async Task<Result<TerritoryDto>> CreateAsync(SaveTerritoryRequest request, int actorId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(request, excludeId: null, cancellationToken);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        var valid = validation.Value!;
        var territory = new TerritoryArea
        {
            MunicipalityId = valid.MunicipalityId,
            Number = valid.Number,
            Name = valid.Name,
            MyMapsId = valid.MapId
        };
        db.Territories.Add(territory);
        return await SaveAsync(territory, created: true, actorId, cancellationToken);
    }

    public async Task<Result<TerritoryDto>> UpdateAsync(int id, SaveTerritoryRequest request, int actorId, CancellationToken cancellationToken)
    {
        var territory = await db.Territories.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (territory is null)
        {
            return AppError.NotFound(NotFoundCode);
        }

        var validation = await ValidateAsync(request, id, cancellationToken);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        Apply(territory, validation.Value!);
        return await SaveAsync(territory, created: false, actorId, cancellationToken);
    }

    /// <summary>Deleting a territory with assignment history requires an explicit second confirmation.</summary>
    public async Task<Result<Unit>> DeleteAsync(int id, bool confirmAssignments, int actorId, CancellationToken cancellationToken)
    {
        // Single statement, so a concurrent first assignment cannot be deleted without confirmation.
        var deleted = await db.Territories
            .Where(t => t.Id == id && (confirmAssignments || !t.Assignments.Any()))
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            LogDeleted(id, actorId, confirmAssignments);
            return Unit.Value;
        }

        var exists = await db.Territories.AnyAsync(t => t.Id == id, cancellationToken);
        return exists ? AppError.Conflict(HasAssignments) : AppError.NotFound(NotFoundCode);
    }

    private static void Apply(TerritoryArea territory, ValidTerritory valid)
    {
        if (territory.MyMapsId != valid.MapId)
        {
            territory.MapKml = null;
            territory.MapFetchedAt = null;
        }

        territory.MunicipalityId = valid.MunicipalityId;
        territory.Number = valid.Number;
        territory.Name = valid.Name;
        territory.MyMapsId = valid.MapId;
    }

    private async Task<Result<TerritoryDto>> SaveAsync(
        TerritoryArea territory, bool created, int actorId, CancellationToken cancellationToken)
    {
        var outcome = await db.TrySaveAsync(cancellationToken);
        if (outcome == SaveOutcome.UniqueViolation)
        {
            return AppError.Conflict(NumberTaken);
        }

        if (outcome == SaveOutcome.ForeignKeyViolation)
        {
            return new ValidationErrors().Add(MunicipalityField, TextRules.NotFound).ToError();
        }

        var id = territory.Id;
        LogSaved(id, actorId, created);
        return await Project(db.Territories.AsNoTracking().Where(t => t.Id == id)).SingleAsync(cancellationToken);
    }

    private async Task<Result<ValidTerritory>> ValidateAsync(SaveTerritoryRequest request, int? excludeId, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        errors.AddIf(request.MunicipalityId is null, MunicipalityField, TextRules.Required);
        ValidateNumber(errors, request.Number);
        var name = TextRules.RequiredText(errors, NameField, request.Name, TerritoryArea.NameMaxLength);
        var mapId = MyMapsLink.Validate(errors, MapUrlField, request.MapUrl);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var municipalityId = request.MunicipalityId!.Value;
        if (!await db.Municipalities.AnyAsync(m => m.Id == municipalityId, cancellationToken))
        {
            return errors.Add(MunicipalityField, TextRules.NotFound).ToError();
        }

        var number = request.Number!.Value;
        var numberTaken = await db.Territories.AnyAsync(
            t => t.MunicipalityId == municipalityId && t.Number == number && (excludeId == null || t.Id != excludeId),
            cancellationToken);
        if (numberTaken)
        {
            return AppError.Conflict(NumberTaken);
        }

        return new ValidTerritory(municipalityId, number, name!, mapId!);
    }

    private static void ValidateNumber(ValidationErrors errors, int? number)
    {
        if (number is null)
        {
            errors.Add(NumberField, TextRules.Required);
            return;
        }

        errors.AddIf(number < 1 || number > MaxNumber, NumberField, TextRules.OutOfRange);
    }

    private static IQueryable<TerritoryDto> Project(IQueryable<TerritoryArea> query) =>
        query.Select(t => new TerritoryDto(
            t.Id,
            t.MunicipalityId,
            t.Municipality!.Name,
            t.Number,
            t.Name,
            MyMapsLink.ToViewerUrl(t.MyMapsId),
            t.Assignments.Count));

    [LoggerMessage(Level = LogLevel.Information, Message = "Territory {TerritoryId} saved by {ActorId} (created: {Created})")]
    private partial void LogSaved(int territoryId, int actorId, bool created);

    [LoggerMessage(Level = LogLevel.Information, Message = "Territory {TerritoryId} deleted by {ActorId} (with assignments: {WithAssignments})")]
    private partial void LogDeleted(int territoryId, int actorId, bool withAssignments);
}
