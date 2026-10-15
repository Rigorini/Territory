namespace Territory.Application.Domain;

public sealed class Municipality
{
    public const int NameMaxLength = 100;

    public int Id { get; set; }
    public required string Name { get; set; }
    public List<TerritoryArea> Territories { get; } = [];
}
