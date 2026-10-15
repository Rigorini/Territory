using Microsoft.AspNetCore.Authorization;
using Territory.Application.Domain;

namespace Territory.Api.Security;

/// <summary>
/// Role matrix. Every authenticated user can assign territories (the default policy of the API group);
/// the policies below only restrict what is reserved to higher roles.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Administrators only: admin users, territories, municipalities and the backup import.</summary>
    public const string Administrator = "administrator";

    /// <summary>Administrators and managers: publishers, backup export and register export.</summary>
    public const string Manager = "manager";

    public static void Register(AuthorizationOptions options)
    {
        options.AddPolicy(Administrator, p => p.RequireRole(nameof(AdminRole.Administrator)));
        options.AddPolicy(Manager, p => p.RequireRole(nameof(AdminRole.Administrator), nameof(AdminRole.Manager)));
    }
}
