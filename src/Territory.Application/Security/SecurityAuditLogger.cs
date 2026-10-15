using Microsoft.Extensions.Logging;
using Territory.Application.Domain;

namespace Territory.Application.Security;

/// <summary>
/// Security audit trail written under a dedicated category, so that it can be persisted regardless of its level.
/// Only identifiers are logged: never usernames, passwords, tokens or request payloads.
/// </summary>
public sealed partial class SecurityAuditLogger(ILoggerFactory loggerFactory)
{
    public const string Category = "Territory.SecurityAudit";
    public const string ReasonUnknownUser = "unknown-user";
    public const string ReasonLocked = "locked";
    public const string ReasonWrongPassword = "wrong-password";
    public const string ReasonInvalidClaims = "invalid-claims";
    public const string ReasonRevoked = "revoked";

    private readonly ILogger logger = loggerFactory.CreateLogger(Category);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Login succeeded for admin {AdminId}")]
    public partial void LoginSucceeded(int adminId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Login failed ({Reason}) for admin {AdminId}")]
    public partial void LoginFailed(string reason, int? adminId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Admin {AdminId} locked out until {LockedUntil}")]
    public partial void AccountLocked(int adminId, DateTimeOffset lockedUntil);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Admin {AdminId} logged out")]
    public partial void LoggedOut(int adminId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "Session rejected ({Reason}) for admin {AdminId}")]
    public partial void SessionRejected(string reason, int? adminId);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Admin {AdminId} created by {ActorId}")]
    public partial void AdminCreated(int adminId, int actorId);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "Admin {AdminId} updated by {ActorId} (password changed: {PasswordChanged})")]
    public partial void AdminUpdated(int adminId, int actorId, bool passwordChanged);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "Admin {AdminId} deleted by {ActorId}")]
    public partial void AdminDeleted(int adminId, int actorId);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "Access denied to admin {AdminId} on {Method} {Route}")]
    public partial void AccessDenied(int? adminId, string method, string? route);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Warning, Message = "Rate limit exceeded on {Method} {Route}")]
    public partial void RateLimitExceeded(string method, string? route);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning, Message = "Antiforgery validation failed for admin {AdminId} on {Method} {Route}")]
    public partial void AntiforgeryValidationFailed(int? adminId, string method, string? route);

    [LoggerMessage(EventId = 1014, Level = LogLevel.Warning, Message = "Admin {AdminId} role changed to {Role} by {ActorId}")]
    public partial void AdminRoleChanged(int adminId, AdminRole role, int actorId);

    [LoggerMessage(EventId = 1015, Level = LogLevel.Information, Message = "Admin {AdminId} changed their own password")]
    public partial void PasswordChanged(int adminId);

    [LoggerMessage(EventId = 1016, Level = LogLevel.Warning, Message = "Password change failed ({Reason}) for admin {AdminId}")]
    public partial void PasswordChangeFailed(string reason, int adminId);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Warning, Message = "First admin {AdminId} created through the initial setup")]
    public partial void FirstAdminCreated(int adminId);
}
