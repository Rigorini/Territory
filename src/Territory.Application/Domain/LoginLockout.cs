namespace Territory.Application.Domain;

/// <summary>
/// Failed password attempts of one account from one client network. Counting per pair means that an attacker who
/// knows a username can only lock that account out for their own network, not for its owner.
/// </summary>
public sealed class LoginLockout
{
    public const int ClientKeyMaxLength = 64;

    public int UserId { get; set; }

    /// <summary>Client network: the IPv4 address, or the /64 prefix of an IPv6 address.</summary>
    public required string ClientKey { get; set; }

    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockoutUntil { get; set; }

    /// <summary>Last failure; rows untouched for a while are purged, which also forgets old isolated failures.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
