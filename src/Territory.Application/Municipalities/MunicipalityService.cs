using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Territory.Application.Abstractions;
using Territory.Application.Common;
using Territory.Application.Domain;

namespace Territory.Application.Municipalities;

public sealed record MunicipalityDto(int Id, string Name, int TerritoryCount);

public sealed record SaveMunicipalityRequest(string? Name);

public sealed partial class MunicipalityService(ITerritoryDbContext db, ILogger<MunicipalityService> logger)
{
    public const string NameField = "name";
    public const string NameTaken = "municipalities.nameTaken";
    public const string NotFoundCode = "municipalities.notFound";
    public const string HasTerritories = "municipalities.hasTerritories";

    public async Task<IReadOnlyList<MunicipalityDto>> ListAsync(CancellationToken cancellationToken) =>
        await db.Municipalities
            .AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new MunicipalityDto(m.Id, m.Name, m.Territories.Count))
            .ToListAsync(cancellationToken);

    public async Task<Result<MunicipalityDto>> CreateAsync(SaveMunicipalityRequest request, int actorId, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var name = TextRules.RequiredText(errors, NameField, request.Name, Municipality.NameMaxLength);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        if (await NameExistsAsync(name!, excludeId: null, cancellationToken))
        {
            return AppError.Conflict(NameTaken);
        }

        var municipality = new Municipality { Name = name! };
        db.Municipalities.Add(municipality);
        if (await db.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AppError.Conflict(NameTaken);
        }

        LogCreated(municipality.Id, actorId);
        return new MunicipalityDto(municipality.Id, municipality.Name, 0);
    }

    public async Task<Result<MunicipalityDto>> UpdateAsync(int id, SaveMunicipalityRequest request, int actorId, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var name = TextRules.RequiredText(errors, NameField, request.Name, Municipality.NameMaxLength);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var municipality = await db.Municipalities.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (municipality is null)
        {
            return AppError.NotFound(NotFoundCode);
        }

        if (await NameExistsAsync(name!, id, cancellationToken))
        {
            return AppError.Conflict(NameTaken);
        }

        municipality.Name = name!;
        if (await db.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AppError.Conflict(NameTaken);
        }

        LogUpdated(id, actorId);
        var territoryCount = await db.Territories.CountAsync(t => t.MunicipalityId == id, cancellationToken);
        return new MunicipalityDto(id, municipality.Name, territoryCount);
    }

    public async Task<Result<Unit>> DeleteAsync(int id, int actorId, CancellationToken cancellationToken)
    {
        // Single statement: the "no territories" rule is evaluated together with the delete.
        var deleted = await db.Municipalities
            .Where(m => m.Id == id && !m.Territories.Any())
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            LogDeleted(id, actorId);
            return Unit.Value;
        }

        var exists = await db.Municipalities.AnyAsync(m => m.Id == id, cancellationToken);
        return exists ? AppError.BusinessRule(HasTerritories) : AppError.NotFound(NotFoundCode);
    }

    private Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken)
    {
        var normalized = TextRules.Normalize(name);
        return db.Municipalities.AnyAsync(
            m => m.Name.ToLower() == normalized && (excludeId == null || m.Id != excludeId),
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Municipality {MunicipalityId} created by {ActorId}")]
    private partial void LogCreated(int municipalityId, int actorId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Municipality {MunicipalityId} updated by {ActorId}")]
    private partial void LogUpdated(int municipalityId, int actorId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Municipality {MunicipalityId} deleted by {ActorId}")]
    private partial void LogDeleted(int municipalityId, int actorId);
}
