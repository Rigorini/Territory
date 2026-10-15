using Territory.Api.Logging;

namespace Territory.Api.Security;

/// <summary>Marks the endpoints a user can call while still forced to choose a new password.</summary>
public sealed class AllowPendingPasswordChange
{
    public static readonly AllowPendingPasswordChange Instance = new();
}

/// <summary>
/// Until the user replaces the password set by the administrator, every API call is refused
/// except the ones needed to do so (profile, password change, logout).
/// </summary>
public sealed class PasswordChangeRequiredFilter : IEndpointFilter
{
    public const string ErrorCode = "auth.passwordChangeRequired";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var allowed = !httpContext.User.MustChangePassword()
            || httpContext.GetEndpoint()?.Metadata.GetMetadata<AllowPendingPasswordChange>() is not null;
        if (allowed)
        {
            return next(context);
        }

        HttpSecurityAudit.AccessDenied(httpContext);
        return ValueTask.FromResult<object?>(Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["code"] = ErrorCode }));
    }
}
