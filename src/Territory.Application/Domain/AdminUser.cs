namespace Territory.Application.Domain;

public sealed class AdminUser
{
    public const int UsernameMaxLength = 50;
    public const int DisplayNameMaxLength = 100;

    public int Id { get; set; }
    public required string Username { get; set; }

    /// <summary>Name of the person, shown in the history; the username is only the login.</summary>
    public required string DisplayName { get; set; }

    /// <summary>What the user may do; see <see cref="AdminRole"/>.</summary>
    public AdminRole Role { get; set; } = AdminRole.Administrator;

    public required string PasswordHash { get; set; }
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();

    /// <summary>Set when an administrator created the account: the user must choose their own password at the first login.</summary>
    public bool MustChangePassword { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockoutUntil { get; set; }
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public string? Language { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Administrator: everything. Manager: assignments, publishers, backup export and register export. Assigner: assignments only.</summary>
public enum AdminRole
{
    Administrator = 1,
    Manager = 2,
    Assigner = 3
}

public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}
