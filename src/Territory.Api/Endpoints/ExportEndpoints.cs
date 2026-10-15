using System.Security.Claims;
using Territory.Api.Security;
using Territory.Application.Export;

namespace Territory.Api.Endpoints;

public static class ExportEndpoints
{
    public static RouteGroupBuilder MapExportEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/export/register", async (
            [AsParameters] RegisterExportRequest request, ClaimsPrincipal user, RegisterExportService service, CancellationToken ct) =>
            (await service.ExportAsync(request, user.GetActorId(), ct))
                .ToHttp(file => Results.File(file.Content, file.ContentType, file.FileName)))
            .RequireAuthorization(AuthorizationPolicies.Manager);
        api.MapGet("/export/s13", async (
            [AsParameters] S13ExportRequest request, ClaimsPrincipal user, S13ExportService service, CancellationToken ct) =>
            (await service.ExportAsync(request, user.GetActorId(), ct))
                .ToHttp(file => Results.File(file.Content, file.ContentType, file.FileName)))
            .RequireAuthorization(AuthorizationPolicies.Manager);
        return api;
    }
}
