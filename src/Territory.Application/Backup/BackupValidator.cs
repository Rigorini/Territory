using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Publishers;
using Territory.Application.Territories;

namespace Territory.Application.Backup;

/// <summary>
/// Checks a backup file against the same rules enforced by the database, so that a restore either applies completely
/// or is rejected before anything is deleted. Problems are reported as codes on the "file" field.
/// </summary>
public static class BackupValidator
{
    public const string FileField = "file";
    public const string InvalidBackup = "invalidBackup";
    public const string UnsupportedVersion = "unsupportedVersion";
    public const string InvalidValues = "invalidValues";
    public const string DuplicateEntries = "duplicateEntries";
    public const string BrokenReference = "brokenReference";
    public const string InvalidAssignments = "invalidAssignments";

    public const string Format = "territory-backup";
    public const int CurrentVersion = 1;

    public static Result<BackupDocument> Validate(BackupDocument? document)
    {
        if (document is not { Format: Format, Municipalities: not null, Territories: not null, Publishers: not null, Assignments: not null })
        {
            return Fail(InvalidBackup);
        }

        if (document.Version != CurrentVersion)
        {
            return Fail(UnsupportedVersion);
        }

        var problems = new HashSet<string>(StringComparer.Ordinal);
        var municipalities = ValidateMunicipalities(document.Municipalities, problems);
        var territories = ValidateTerritories(document.Territories, municipalities, problems);
        var publishers = ValidatePublishers(document.Publishers, problems);
        var assignments = ValidateAssignments(document.Assignments, territories, publishers, problems);
        if (problems.Count > 0)
        {
            return Fail([.. problems]);
        }

        return document with
        {
            Municipalities = municipalities.Values.ToList(),
            Territories = territories.Values.ToList(),
            Publishers = publishers.Values.ToList(),
            Assignments = assignments
        };
    }

    private static Result<BackupDocument> Fail(params string[] codes)
    {
        var errors = new ValidationErrors();
        foreach (var code in codes)
        {
            errors.Add(FileField, code);
        }

        return errors.ToError();
    }

    private static Dictionary<int, BackupMunicipality> ValidateMunicipalities(
        IReadOnlyList<BackupMunicipality> items, HashSet<string> problems)
    {
        var result = new Dictionary<int, BackupMunicipality>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var name = CleanText(item?.Name, Municipality.NameMaxLength, problems);
            if (item?.Id is not { } id || name is null)
            {
                problems.Add(InvalidValues);
                continue;
            }

            if (!names.Add(TextRules.Normalize(name)) || !result.TryAdd(id, item with { Name = name }))
            {
                problems.Add(DuplicateEntries);
            }
        }

        return result;
    }

    private static Dictionary<int, BackupTerritory> ValidateTerritories(
        IReadOnlyList<BackupTerritory> items, Dictionary<int, BackupMunicipality> municipalities, HashSet<string> problems)
    {
        var result = new Dictionary<int, BackupTerritory>();
        var numbers = new HashSet<(int, int)>();
        foreach (var item in items)
        {
            var name = CleanText(item?.Name, TerritoryArea.NameMaxLength, problems);
            if (item is not { Id: { } id, MunicipalityId: { } municipalityId, Number: { } number } || name is null || !IsValidTerritory(item))
            {
                problems.Add(InvalidValues);
                continue;
            }

            problems.AddIf(!municipalities.ContainsKey(municipalityId), BrokenReference);
            if (!numbers.Add((municipalityId, number)) || !result.TryAdd(id, item with { Name = name }))
            {
                problems.Add(DuplicateEntries);
            }
        }

        return result;
    }

    private static bool IsValidTerritory(BackupTerritory item) =>
        item.Number is >= 1 and <= TerritoryService.MaxNumber && MyMapsLink.IsValidMapId(item.MyMapsId);

    private static Dictionary<int, BackupPublisher> ValidatePublishers(IReadOnlyList<BackupPublisher> items, HashSet<string> problems)
    {
        var result = new Dictionary<int, BackupPublisher>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var firstName = CleanText(item?.FirstName, Publisher.NameMaxLength, problems);
            var lastName = CleanText(item?.LastName, Publisher.NameMaxLength, problems);
            if (item?.Id is not { } id || firstName is null || lastName is null)
            {
                problems.Add(InvalidValues);
                continue;
            }

            var isDeleted = item.IsDeleted ?? false;
            var publisher = item with { FirstName = firstName, LastName = lastName, IsDeleted = isDeleted, DeletedAt = isDeleted ? item.DeletedAt : null };
            if (!identities.Add(PublisherService.NameIdentity(firstName, lastName)) || !result.TryAdd(id, publisher))
            {
                problems.Add(DuplicateEntries);
            }
        }

        return result;
    }

    private static List<BackupAssignment> ValidateAssignments(
        IReadOnlyList<BackupAssignment> items,
        Dictionary<int, BackupTerritory> territories,
        Dictionary<int, BackupPublisher> publishers,
        HashSet<string> problems)
    {
        var result = new List<BackupAssignment>(items.Count);
        var openTerritories = new HashSet<int>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is not { TerritoryId: { } territoryId, PublisherId: { } publisherId, AssignedAt: not null })
            {
                problems.Add(InvalidValues);
                continue;
            }

            problems.AddIf(!territories.ContainsKey(territoryId) || !publishers.ContainsKey(publisherId), BrokenReference);
            problems.AddIf(!IsConsistent(item), InvalidAssignments);
            var isOpen = item.ReturnedAt is null;
            var duplicate = (isOpen && !openTerritories.Add(territoryId))
                || (item.TokenHash is not null && !tokens.Add(Convert.ToBase64String(item.TokenHash)));
            problems.AddIf(duplicate, DuplicateEntries);
            result.Add(item with { ReturnedByName = CleanOptionalText(item.ReturnedByName, problems) });
        }

        return result;
    }

    /// <summary>Mirrors the assignment check constraints: an open assignment holds a share token, a returned one does not.</summary>
    private static bool IsConsistent(BackupAssignment item)
    {
        var isOpen = item.ReturnedAt is null;
        var datesOk = isOpen || item.ReturnedAt >= item.AssignedAt;
        var tokenOk = isOpen ? item.TokenHash is { Length: > 0 } : item.TokenHash is null;
        var returnOk = !isOpen || (item.ReturnedBy is null && item.ReturnedByName is null);
        return datesOk && tokenOk && returnOk;
    }

    private static void AddIf(this HashSet<string> problems, bool condition, string code)
    {
        if (condition)
        {
            problems.Add(code);
        }
    }

    private static string? CleanOptionalText(string? value, HashSet<string> problems) =>
        string.IsNullOrWhiteSpace(value) ? null : CleanText(value, AdminUser.DisplayNameMaxLength, problems);

    private static string? CleanText(string? value, int maxLength, HashSet<string> problems)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > maxLength || text.Any(char.IsControl))
        {
            problems.Add(InvalidValues);
            return null;
        }

        return text;
    }
}
