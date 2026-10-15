using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Territory.Application.Abstractions;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Territories;

namespace Territory.Application.Assignments;

public sealed record OpenAssignmentDto(long Id, int PublisherId, string PublisherName, DateTimeOffset AssignedAt);

/// <summary>Current state of a territory: its open assignment, the date it was last returned and how many times it was assigned in the last 365 days.</summary>
public sealed record TerritoryStatusDto(
    int TerritoryId,
    int MunicipalityId,
    string MunicipalityName,
    int Number,
    string Name,
    OpenAssignmentDto? Assignment,
    DateTimeOffset? LastReturnedAt,
    int AssignmentsLastYear);

public sealed record AssignRequest(int? TerritoryId, int? PublisherId, DateTimeOffset? AssignedAt);

public sealed record ReturnRequest(DateTimeOffset? ReturnedAt);

/// <summary>Corrects the dates of an assignment; ReturnedAt must be null for an open assignment and set for a returned one.</summary>
public sealed record UpdateDatesRequest(DateTimeOffset? AssignedAt, DateTimeOffset? ReturnedAt);

/// <summary>The clear share token: shown only in this response, never stored nor logged.</summary>
public sealed record ShareLinkDto(long AssignmentId, string Token);

/// <summary>
/// ReturnedBy is "publisher" or "admin" for returned assignments; ReturnedByName is the name of the admin
/// who registered the return (null for publishers and for returns registered before names were recorded).
/// </summary>
public sealed record AssignmentHistoryDto(
    long Id, string PublisherName, DateTimeOffset AssignedAt, DateTimeOffset? ReturnedAt, string? ReturnedBy,
    string? ReturnedByName);

public sealed partial class AssignmentService(ITerritoryDbContext db, TimeProvider clock, ILogger<AssignmentService> logger)
{
    public const string TerritoryField = "territoryId";
    public const string PublisherField = "publisherId";
    public const string AssignedAtField = "assignedAt";
    public const string ReturnedAtField = "returnedAt";
    public const string BeforeLastReturn = "beforeLastReturn";
    public const string BeforeAssigned = "beforeAssigned";
    public const string AfterNextAssignment = "afterNextAssignment";
    public const string NotFoundCode = "assignments.notFound";
    public const string AlreadyAssigned = "assignments.alreadyAssigned";
    public const string AlreadyReturned = "assignments.alreadyReturned";

    private sealed record ValidAssignment(int TerritoryId, int PublisherId, DateTimeOffset AssignedAt);

    private sealed record ValidDates(DateTimeOffset AssignedAt, DateTimeOffset? ReturnedAt);

    public const int AverageWindowDays = 365;

    public async Task<IReadOnlyList<TerritoryStatusDto>> ListStatusAsync(CancellationToken cancellationToken)
    {
        var since = clock.GetUtcNow().AddDays(-AverageWindowDays);
        var rows = await db.Territories
            .AsNoTracking()
            .OrderBy(t => t.Municipality!.Name)
            .ThenBy(t => t.Number)
            .Select(t => new
            {
                t.Id,
                t.MunicipalityId,
                MunicipalityName = t.Municipality!.Name,
                t.Number,
                t.Name,
                Open = t.Assignments
                    .Where(a => a.ReturnedAt == null)
                    .Select(a => new { a.Id, a.PublisherId, a.Publisher!.FirstName, a.Publisher.LastName, a.AssignedAt })
                    .FirstOrDefault(),
                LastReturnedAt = t.Assignments.Max(a => a.ReturnedAt),
                LastYear = t.Assignments.Count(a => a.AssignedAt >= since)
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TerritoryStatusDto(
                r.Id,
                r.MunicipalityId,
                r.MunicipalityName,
                r.Number,
                r.Name,
                r.Open is null ? null : new OpenAssignmentDto(r.Open.Id, r.Open.PublisherId, FullName(r.Open.FirstName, r.Open.LastName), r.Open.AssignedAt),
                r.LastReturnedAt,
                r.LastYear))
            .ToList();
    }

    public async Task<Result<IReadOnlyList<AssignmentHistoryDto>>> HistoryAsync(int territoryId, CancellationToken cancellationToken)
    {
        if (!await db.Territories.AnyAsync(t => t.Id == territoryId, cancellationToken))
        {
            return AppError.NotFound(TerritoryService.NotFoundCode);
        }

        var rows = await db.Assignments
            .AsNoTracking()
            .Where(a => a.TerritoryId == territoryId)
            .OrderByDescending(a => a.AssignedAt)
            .Select(a => new { a.Id, a.Publisher!.FirstName, a.Publisher.LastName, a.AssignedAt, a.ReturnedAt, a.ReturnedBy, a.ReturnedByName })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new AssignmentHistoryDto(
                r.Id, FullName(r.FirstName, r.LastName), r.AssignedAt, r.ReturnedAt, ToCode(r.ReturnedBy), r.ReturnedByName))
            .ToList();
    }

    /// <summary>Opens an assignment and issues its share token.</summary>
    public async Task<Result<ShareLinkDto>> AssignAsync(AssignRequest request, int actorId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(request, cancellationToken);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        var valid = validation.Value!;
        var issued = ShareToken.Issue();
        var assignment = new Assignment
        {
            TerritoryId = valid.TerritoryId,
            PublisherId = valid.PublisherId,
            AssignedAt = valid.AssignedAt,
            TokenHash = issued.Hash
        };
        db.Assignments.Add(assignment);

        // The partial unique index (one open assignment per territory) settles concurrent requests.
        var outcome = await db.TrySaveAsync(cancellationToken);
        if (outcome != SaveOutcome.Saved)
        {
            return outcome == SaveOutcome.UniqueViolation
                ? AppError.Conflict(AlreadyAssigned)
                : AppError.NotFound(TerritoryService.NotFoundCode);
        }

        LogAssigned(assignment.Id, valid.TerritoryId, valid.PublisherId, actorId);
        return new ShareLinkDto(assignment.Id, issued.Token);
    }

    /// <summary>Replaces the share token of an open assignment: the previous link stops working.</summary>
    public async Task<Result<ShareLinkDto>> RenewLinkAsync(long id, int actorId, CancellationToken cancellationToken)
    {
        var issued = ShareToken.Issue();
        var updated = await db.Assignments
            .Where(a => a.Id == id && a.ReturnedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.TokenHash, issued.Hash), cancellationToken);
        if (updated == 0)
        {
            return await ClosedOrMissingAsync(id, cancellationToken);
        }

        LogLinkRenewed(id, actorId);
        return new ShareLinkDto(id, issued.Token);
    }

    /// <summary>Records the return on behalf of the publisher and invalidates the share link.</summary>
    public async Task<Result<Unit>> ReturnAsync(long id, ReturnRequest request, int actorId, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var returnedAt = DateRules.PastOrNow(errors, ReturnedAtField, request.ReturnedAt, clock.GetUtcNow());
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        // Single statement: the open state and the date order are checked atomically.
        var updated = await db.Assignments
            .Where(a => a.Id == id && a.ReturnedAt == null && a.AssignedAt <= returnedAt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.ReturnedAt, returnedAt)
                .SetProperty(a => a.ReturnedBy, ReturnedBy.Admin)
                .SetProperty(a => a.ReturnedByName, a => db.AdminUsers.Where(u => u.Id == actorId).Select(u => u.DisplayName).FirstOrDefault())
                .SetProperty(a => a.TokenHash, (byte[]?)null), cancellationToken);
        if (updated > 0)
        {
            LogReturned(id, actorId);
            return Unit.Value;
        }

        var isOpen = await db.Assignments.AnyAsync(a => a.Id == id && a.ReturnedAt == null, cancellationToken);
        return isOpen
            ? errors.Add(ReturnedAtField, BeforeAssigned).ToError()
            : (await ClosedOrMissingAsync(id, cancellationToken)).Error!;
    }

    /// <summary>
    /// Corrects the dates of an assignment, keeping the territory history consistent:
    /// periods of the same territory may touch but never overlap. The open/returned state is not changed.
    /// </summary>
    public async Task<Result<Unit>> UpdateDatesAsync(long id, UpdateDatesRequest request, int actorId, CancellationToken cancellationToken)
    {
        var current = await db.Assignments
            .Where(a => a.Id == id)
            .Select(a => new { a.TerritoryId, IsOpen = a.ReturnedAt == null })
            .SingleOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return AppError.NotFound(NotFoundCode);
        }

        var validation = ValidateDates(request, current.IsOpen);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        var dates = validation.Value!;
        var overlapping = Overlapping(current.TerritoryId, id, dates);

        // Single statement: the state and the absence of overlaps are checked atomically with the update.
        var updated = await db.Assignments
            .Where(a => a.Id == id && (a.ReturnedAt == null) == current.IsOpen && !overlapping.Any())
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.AssignedAt, dates.AssignedAt)
                .SetProperty(a => a.ReturnedAt, dates.ReturnedAt), cancellationToken);
        if (updated > 0)
        {
            LogDatesUpdated(id, actorId);
            return Unit.Value;
        }

        var stateUnchanged = await db.Assignments.AnyAsync(a => a.Id == id && (a.ReturnedAt == null) == current.IsOpen, cancellationToken);
        return stateUnchanged
            ? await OverlapErrorAsync(overlapping, dates, cancellationToken)
            : (await ClosedOrMissingAsync(id, cancellationToken)).Error!;
    }

    private Result<ValidDates> ValidateDates(UpdateDatesRequest request, bool isOpen)
    {
        var errors = new ValidationErrors();
        var now = clock.GetUtcNow();
        errors.AddIf(request.AssignedAt is null, AssignedAtField, TextRules.Required);
        errors.AddIf(isOpen && request.ReturnedAt is not null, ReturnedAtField, TextRules.InvalidFormat);
        errors.AddIf(!isOpen && request.ReturnedAt is null, ReturnedAtField, TextRules.Required);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var assignedAt = DateRules.PastOrNow(errors, AssignedAtField, request.AssignedAt, now);
        DateTimeOffset? returnedAt = isOpen ? null : DateRules.PastOrNow(errors, ReturnedAtField, request.ReturnedAt, now);
        errors.AddIf(returnedAt < assignedAt, ReturnedAtField, BeforeAssigned);
        return errors.HasErrors ? errors.ToError() : new ValidDates(assignedAt, returnedAt);
    }

    /// <summary>Other assignments of the territory overlapping the given period (an open one lasts until now).</summary>
    private IQueryable<Assignment> Overlapping(int territoryId, long id, ValidDates dates) =>
        db.Assignments.Where(o => o.TerritoryId == territoryId
            && o.Id != id
            && (o.ReturnedAt == null || o.ReturnedAt > dates.AssignedAt)
            && (dates.ReturnedAt == null || o.AssignedAt < dates.ReturnedAt));

    // An overlap with an older period is reported on the start, with a newer one on the end.
    private static async Task<AppError> OverlapErrorAsync(
        IQueryable<Assignment> overlapping, ValidDates dates, CancellationToken cancellationToken)
    {
        var starts = await overlapping.Select(o => o.AssignedAt).ToListAsync(cancellationToken);
        var errors = new ValidationErrors();
        var hitsNewer = dates.ReturnedAt is not null && starts.Exists(start => start > dates.AssignedAt);
        errors.AddIf(!hitsNewer || starts.Exists(start => start <= dates.AssignedAt), AssignedAtField, BeforeLastReturn);
        errors.AddIf(hitsNewer, ReturnedAtField, AfterNextAssignment);
        return errors.ToError();
    }

    private async Task<Result<ShareLinkDto>> ClosedOrMissingAsync(long id, CancellationToken cancellationToken)
    {
        var exists = await db.Assignments.AnyAsync(a => a.Id == id, cancellationToken);
        return exists ? AppError.Conflict(AlreadyReturned) : AppError.NotFound(NotFoundCode);
    }

    private async Task<Result<ValidAssignment>> ValidateAsync(AssignRequest request, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        errors.AddIf(request.TerritoryId is null, TerritoryField, TextRules.Required);
        errors.AddIf(request.PublisherId is null, PublisherField, TextRules.Required);
        var assignedAt = DateRules.PastOrNow(errors, AssignedAtField, request.AssignedAt, clock.GetUtcNow());
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var territoryId = request.TerritoryId!.Value;
        var publisherId = request.PublisherId!.Value;
        var territory = await db.Territories
            .Where(t => t.Id == territoryId)
            .Select(t => new
            {
                IsAssigned = t.Assignments.Any(a => a.ReturnedAt == null),
                LastReturnedAt = t.Assignments.Max(a => a.ReturnedAt)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var publisherActive = await db.Publishers.AnyAsync(p => p.Id == publisherId && !p.IsDeleted, cancellationToken);

        errors.AddIf(territory is null, TerritoryField, TextRules.NotFound);
        errors.AddIf(!publisherActive, PublisherField, TextRules.NotFound);
        errors.AddIf(territory?.LastReturnedAt > assignedAt, AssignedAtField, BeforeLastReturn);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        return territory!.IsAssigned
            ? AppError.Conflict(AlreadyAssigned)
            : new ValidAssignment(territoryId, publisherId, assignedAt);
    }

    private static string FullName(string firstName, string lastName) => $"{lastName} {firstName}";

    private static string? ToCode(ReturnedBy? returnedBy) => returnedBy switch
    {
        ReturnedBy.Publisher => "publisher",
        ReturnedBy.Admin => "admin",
        _ => null
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Assignment {AssignmentId} opened for territory {TerritoryId} and publisher {PublisherId} by {ActorId}")]
    private partial void LogAssigned(long assignmentId, int territoryId, int publisherId, int actorId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Share link of assignment {AssignmentId} renewed by {ActorId}")]
    private partial void LogLinkRenewed(long assignmentId, int actorId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Assignment {AssignmentId} returned by admin {ActorId}")]
    private partial void LogReturned(long assignmentId, int actorId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dates of assignment {AssignmentId} corrected by admin {ActorId}")]
    private partial void LogDatesUpdated(long assignmentId, int actorId);
}
