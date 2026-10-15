using Territory.Application.Common;

namespace Territory.Api.Endpoints;

public static class ResultMapping
{
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value!) : result.Error!.ToHttp();

    public static IResult ToHttp(this AppError error)
    {
        if (error.Kind == ErrorKind.Validation)
        {
            return Results.ValidationProblem(
                error.Fields ?? new Dictionary<string, string[]>(),
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });
        }

        return Results.Problem(
            statusCode: ToStatusCode(error.Kind),
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    public static int ToStatusCode(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };
}
