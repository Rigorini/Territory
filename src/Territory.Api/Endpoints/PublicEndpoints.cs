using Territory.Api.Security;
using Territory.Application.Public;

namespace Territory.Api.Endpoints;

/// <summary>
/// Anonymous endpoints of the publisher page. The share token travels in a header, never in the API URL,
/// so it does not end up in access logs; a custom header also forces a CORS preflight for cross-site requests.
/// </summary>
public static class PublicEndpoints
{
    public const string TokenHeader = "X-Share-Token";

    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/territory")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PublicToken);

        group.MapGet("/", async (HttpRequest request, PublicTerritoryService service, CancellationToken ct) =>
            (await service.GetAsync(Token(request), ct)).ToHttp(Results.Ok));

        group.MapGet("/map", async (HttpRequest request, PublicTerritoryService service, CancellationToken ct) =>
            (await service.GetMapAsync(Token(request), ct)).ToHttp(Results.Ok));

        group.MapPost("/return", async (HttpRequest request, PublicTerritoryService service, CancellationToken ct) =>
            (await service.ReturnAsync(Token(request), ct)).ToHttp(Results.Ok));

        return app;
    }

    private static string? Token(HttpRequest request) =>
        request.Headers.TryGetValue(TokenHeader, out var values) && values.Count == 1 ? values[0] : null;
}
