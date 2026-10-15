namespace Territory.Application.Domain;

public sealed class Assignment
{
    public long Id { get; set; }
    public int TerritoryId { get; set; }
    public TerritoryArea? Territory { get; set; }
    public int PublisherId { get; set; }
    public Publisher? Publisher { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    public ReturnedBy? ReturnedBy { get; set; }

    /// <summary>Name of the admin who registered the return, copied at that time (null for publishers and legacy rows).</summary>
    public string? ReturnedByName { get; set; }

    /// <summary>SHA-256 of the share token; the clear token is never persisted.</summary>
    public byte[]? TokenHash { get; set; }

    public bool IsOpen => ReturnedAt is null;
}

public enum ReturnedBy
{
    Publisher = 1,
    Admin = 2
}
