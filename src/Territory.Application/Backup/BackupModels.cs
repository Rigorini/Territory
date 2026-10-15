using System.Text.Json.Serialization;
using Territory.Application.Domain;

namespace Territory.Application.Backup;

/// <summary>
/// Portable copy of the registry (never of the admin accounts). Personal names are stored in clear text:
/// the file is sensitive and must be handled like the database itself. Ids only link the entries inside the file.
/// Every member is nullable so that a malformed file is reported as a validation error instead of failing deserialization.
/// </summary>
public sealed record BackupDocument(
    string? Format,
    int? Version,
    DateTimeOffset? ExportedAt,
    IReadOnlyList<BackupMunicipality>? Municipalities,
    IReadOnlyList<BackupTerritory>? Territories,
    IReadOnlyList<BackupPublisher>? Publishers,
    IReadOnlyList<BackupAssignment>? Assignments);

public sealed record BackupMunicipality(int? Id, string? Name);

public sealed record BackupTerritory(int? Id, int? MunicipalityId, int? Number, string? Name, string? MyMapsId);

public sealed record BackupPublisher(int? Id, string? FirstName, string? LastName, bool? IsDeleted, DateTimeOffset? DeletedAt);

/// <summary>The share link token itself is never stored: only its hash, so links already handed out keep working after a restore.</summary>
public sealed record BackupAssignment(
    int? TerritoryId,
    int? PublisherId,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? ReturnedAt,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ReturnedBy>))] ReturnedBy? ReturnedBy,
    string? ReturnedByName,
    byte[]? TokenHash);

public sealed record BackupImportSummary(int Municipalities, int Territories, int Publishers, int Assignments);
