using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Territory.Application.Abstractions;
using Territory.Application.Common;
using Territory.Application.Domain;

namespace Territory.Application.Publishers;

public sealed record PublisherDto(int Id, string FirstName, string LastName);

public sealed record SavePublisherRequest(string? FirstName, string? LastName);

/// <summary><see cref="Reactivated"/> is true when a logically deleted publisher with the same name was restored.</summary>
public sealed record CreatePublisherResult(PublisherDto Publisher, bool Reactivated);

/// <summary>Publishers are deleted logically so their names remain in the assignment history.</summary>
public sealed partial class PublisherService(
    ITerritoryDbContext db, TimeProvider clock, ILogger<PublisherService> logger, IPersonalDataCipher protector)
{
    public const string FirstNameField = "firstName";
    public const string LastNameField = "lastName";
    public const string Duplicate = "publishers.duplicate";
    public const string DuplicateDeleted = "publishers.duplicateDeleted";
    public const string NotFoundCode = "publishers.notFound";
    public const string HasOpenAssignment = "publishers.hasOpenAssignment";

    private sealed record ValidName(string FirstName, string LastName);

    public async Task<IReadOnlyList<PublisherDto>> ListAsync(CancellationToken cancellationToken)
    {
        var publishers = await db.Publishers
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Select(p => new PublisherDto(p.Id, p.FirstName, p.LastName))
            .ToListAsync(cancellationToken);
        return publishers
            .OrderBy(p => p.LastName, NameOrdering.Comparer)
            .ThenBy(p => p.FirstName, NameOrdering.Comparer)
            .ToList();
    }

    public async Task<Result<CreatePublisherResult>> CreateAsync(SavePublisherRequest request, int actorId, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        var name = validation.Value!;
        var publisher = await FindByNameAsync(name, cancellationToken);
        if (publisher is { IsDeleted: false })
        {
            return AppError.Conflict(Duplicate);
        }

        var reactivated = publisher is not null;
        publisher ??= db.Publishers.Add(new Publisher { FirstName = name.FirstName, LastName = name.LastName, NameHash = NameHash(name) }).Entity;
        Apply(publisher, name);
        if (await db.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AppError.Conflict(Duplicate);
        }

        LogCreated(publisher.Id, actorId, reactivated);
        return new CreatePublisherResult(ToDto(publisher), reactivated);
    }

    public async Task<Result<PublisherDto>> UpdateAsync(int id, SavePublisherRequest request, int actorId, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        var publisher = await db.Publishers.SingleOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
        if (publisher is null)
        {
            return AppError.NotFound(NotFoundCode);
        }

        var name = validation.Value!;
        var homonym = await FindByNameAsync(name, cancellationToken);
        if (homonym is not null && homonym.Id != id)
        {
            return AppError.Conflict(homonym.IsDeleted ? DuplicateDeleted : Duplicate);
        }

        Apply(publisher, name);
        if (await db.TrySaveAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AppError.Conflict(Duplicate);
        }

        LogUpdated(id, actorId);
        return ToDto(publisher);
    }

    public async Task<Result<Unit>> DeleteAsync(int id, int actorId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        // Single statement: a publisher holding a territory cannot be deleted, even under concurrency.
        var deleted = await db.Publishers
            .Where(p => p.Id == id && !p.IsDeleted
                && !db.Assignments.Any(a => a.PublisherId == p.Id && a.ReturnedAt == null))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDeleted, true).SetProperty(p => p.DeletedAt, now), cancellationToken);
        if (deleted > 0)
        {
            LogDeleted(id, actorId);
            return Unit.Value;
        }

        var active = await db.Publishers.AnyAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
        return active ? AppError.BusinessRule(HasOpenAssignment) : AppError.NotFound(NotFoundCode);
    }

    private static Result<ValidName> Validate(SavePublisherRequest request)
    {
        var errors = new ValidationErrors();
        var firstName = TextRules.RequiredText(errors, FirstNameField, request.FirstName, Publisher.NameMaxLength);
        var lastName = TextRules.RequiredText(errors, LastNameField, request.LastName, Publisher.NameMaxLength);
        return errors.HasErrors ? errors.ToError() : new ValidName(firstName!, lastName!);
    }

    private Task<Publisher?> FindByNameAsync(ValidName name, CancellationToken cancellationToken)
    {
        var hash = NameHash(name);
        return db.Publishers.SingleOrDefaultAsync(p => p.NameHash == hash, cancellationToken);
    }

    /// <summary>Case-insensitive identity of a person; the separator avoids ambiguity between ("ab", "c") and ("a", "bc").</summary>
    public static string NameIdentity(string firstName, string lastName) =>
        $"{TextRules.Normalize(firstName)}\u001f{TextRules.Normalize(lastName)}";

    private string NameHash(ValidName name) => protector.BlindIndex(NameIdentity(name.FirstName, name.LastName));

    private void Apply(Publisher publisher, ValidName name)
    {
        publisher.FirstName = name.FirstName;
        publisher.LastName = name.LastName;
        publisher.NameHash = NameHash(name);
        publisher.IsDeleted = false;
        publisher.DeletedAt = null;
    }

    private static PublisherDto ToDto(Publisher publisher) => new(publisher.Id, publisher.FirstName, publisher.LastName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Publisher {PublisherId} created by {ActorId} (reactivated: {Reactivated})")]
    private partial void LogCreated(int publisherId, int actorId, bool reactivated);

    [LoggerMessage(Level = LogLevel.Information, Message = "Publisher {PublisherId} updated by {ActorId}")]
    private partial void LogUpdated(int publisherId, int actorId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Publisher {PublisherId} logically deleted by {ActorId}")]
    private partial void LogDeleted(int publisherId, int actorId);
}
