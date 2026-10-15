namespace Territory.Application.Domain;

public sealed class Publisher
{
    public const int NameMaxLength = 60;
    public const int NameHashLength = 64;

    public int Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }

    /// <summary>Keyed hash of the normalized name: enforces uniqueness while the name itself is stored encrypted.</summary>
    public required string NameHash { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
