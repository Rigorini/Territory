using System.Security.Claims;
using Territory.Api.Security;
using Territory.Application.Assignments;

namespace Territory.Api.Endpoints;

/// <summary>Territory assignments: status board, assign, renew share link, admin return and history.</summary>
public static class AssignmentEndpoints
{
    public static RouteGroupBuilder MapAssignmentEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/assignments");

        group.MapGet("/", async (AssignmentService service, CancellationToken ct) => Results.Ok(await service.ListStatusAsync(ct)));

        group.MapPost("/", async (AssignRequest request, ClaimsPrincipal user, AssignmentService service, CancellationToken ct) =>
            (await service.AssignAsync(request, user.GetActorId(), ct))
                .ToHttp(link => Results.Created($"/api/assignments/{link.AssignmentId}", link)));

        group.MapPost("/{id:long}/link", async (long id, ClaimsPrincipal user, AssignmentService service, CancellationToken ct) =>
            (await service.RenewLinkAsync(id, user.GetActorId(), ct)).ToHttp(Results.Ok));

        group.MapPost("/{id:long}/return", async (long id, ReturnRequest request, ClaimsPrincipal user, AssignmentService service, CancellationToken ct) =>
            (await service.ReturnAsync(id, request, user.GetActorId(), ct)).ToHttp(_ => Results.NoContent()));

        group.MapPut("/{id:long}", async (long id, UpdateDatesRequest request, ClaimsPrincipal user, AssignmentService service, CancellationToken ct) =>
            (await service.UpdateDatesAsync(id, request, user.GetActorId(), ct)).ToHttp(_ => Results.NoContent()));

        api.MapGet("/territories/{id:int}/assignments", async (int id, AssignmentService service, CancellationToken ct) =>
            (await service.HistoryAsync(id, ct)).ToHttp(Results.Ok));

        return api;
    }
}
