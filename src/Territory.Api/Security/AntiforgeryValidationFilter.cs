using Microsoft.AspNetCore.Antiforgery;
using Territory.Api.Logging;

namespace Territory.Api.Security;

/// <summary>
/// Validates the antiforgery header on state-changing API calls (JSON endpoints are not covered by UseAntiforgery).
/// </summary>
public sealed class AntiforgeryValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public const string ErrorCode = "auth.antiforgery";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (IsSafeMethod(httpContext.Request.Method) || await antiforgery.IsRequestValidAsync(httpContext))
        {
            return await next(context);
        }

        HttpSecurityAudit.AntiforgeryValidationFailed(httpContext);
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = ErrorCode });
    }

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
}
