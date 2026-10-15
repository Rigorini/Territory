namespace Territory.Application.Domain;

public sealed class TerritoryArea
{
    public const int NameMaxLength = 100;
    public const int MapIdMaxLength = 100;

    public int Id { get; set; }
    public int MunicipalityId { get; set; }
    public Municipality? Municipality { get; set; }
    public int Number { get; set; }
    public required string Name { get; set; }

    /// <summary>Only the MyMaps "mid" is stored, never the full URL, so the server rebuilds the KML address itself.</summary>
    public required string MyMapsId { get; set; }

    public string? MapKml { get; set; }
    public DateTimeOffset? MapFetchedAt { get; set; }
    public List<Assignment> Assignments { get; } = [];
}
