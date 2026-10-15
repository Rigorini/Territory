using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Territory.Api.Security;
using Territory.Application.Backup;

namespace Territory.Api.Endpoints;

public static class BackupEndpoints
{
    /// <summary>Backups hold the whole registry, so the global 1 MB request limit is raised for the import only.</summary>
    public const long MaxImportBytes = 50L * 1024 * 1024;

    public static RouteGroupBuilder MapBackupEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/backup");

        group.MapGet("/export", async (ClaimsPrincipal user, BackupService service, CancellationToken ct) =>
        {
            var file = await service.ExportAsync(user.GetActorId(), ct);
            return Results.File(file.Content, file.ContentType, file.FileName);
        }).RequireAuthorization(AuthorizationPolicies.Manager);

        group.MapPost("/import", async (
            HttpRequest request, [FromQuery] bool? confirmOverwrite, ClaimsPrincipal user, BackupService service, CancellationToken ct) =>
            (await service.ImportAsync(request.Body, confirmOverwrite == true, user.GetActorId(), ct))
                .ToHttp(summary => Results.Ok(summary)))
            .WithMetadata(new RequestSizeLimitAttribute(MaxImportBytes))
            .RequireAuthorization(AuthorizationPolicies.Administrator);

        return api;
    }
}
