using System.Security.Claims;
using Territory.Api.Security;
using Territory.Application.Admins;

namespace Territory.Api.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/admins").RequireAuthorization(AuthorizationPolicies.Administrator);

        group.MapGet("/", async (AdminService admins, CancellationToken ct) => Results.Ok(await admins.ListAsync(ct)));

        group.MapPost("/", async (SaveAdminRequest request, ClaimsPrincipal user, AdminService admins, CancellationToken ct) =>
            (await admins.CreateAsync(request, user.GetActorId(), ct))
                .ToHttp(dto => Results.Created($"/api/admins/{dto.Id}", dto)));

        group.MapPut("/{id:int}", async (int id, SaveAdminRequest request, ClaimsPrincipal user, AdminService admins, CancellationToken ct) =>
            (await admins.UpdateAsync(id, request, user.GetActorId(), ct)).ToHttp(Results.Ok));

        group.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, AdminService admins, CancellationToken ct) =>
            (await admins.DeleteAsync(id, user.GetActorId(), ct)).ToHttp(_ => Results.NoContent()));

        return api;
    }
}
