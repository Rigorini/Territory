using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Territory.Api.Security;
using Territory.Application.Municipalities;
using Territory.Application.Publishers;
using Territory.Application.Territories;

namespace Territory.Api.Endpoints;

/// <summary>Master data: municipalities, territories and publishers.</summary>
public static class RegistryEndpoints
{
    public static RouteGroupBuilder MapRegistryEndpoints(this RouteGroupBuilder api)
    {
        MapMunicipalities(api.MapGroup("/municipalities"));
        MapTerritories(api.MapGroup("/territories"));
        MapPublishers(api.MapGroup("/publishers"));
        return api;
    }

    private static void MapMunicipalities(RouteGroupBuilder group)
    {
        group.MapGet("/", async (MunicipalityService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)));

        var write = group.MapGroup(string.Empty).RequireAuthorization(AuthorizationPolicies.Administrator);

        write.MapPost("/", async (SaveMunicipalityRequest request, ClaimsPrincipal user, MunicipalityService service, CancellationToken ct) =>
            (await service.CreateAsync(request, user.GetActorId(), ct))
                .ToHttp(dto => Results.Created($"/api/municipalities/{dto.Id}", dto)));

        write.MapPut("/{id:int}", async (int id, SaveMunicipalityRequest request, ClaimsPrincipal user, MunicipalityService service, CancellationToken ct) =>
            (await service.UpdateAsync(id, request, user.GetActorId(), ct)).ToHttp(Results.Ok));

        write.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, MunicipalityService service, CancellationToken ct) =>
            (await service.DeleteAsync(id, user.GetActorId(), ct)).ToHttp(_ => Results.NoContent()));
    }

    private static void MapTerritories(RouteGroupBuilder group)
    {
        group.MapGet("/", async (TerritoryService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)));

        var write = group.MapGroup(string.Empty).RequireAuthorization(AuthorizationPolicies.Administrator);

        write.MapPost("/", async (SaveTerritoryRequest request, ClaimsPrincipal user, TerritoryService service, CancellationToken ct) =>
            (await service.CreateAsync(request, user.GetActorId(), ct))
                .ToHttp(dto => Results.Created($"/api/territories/{dto.Id}", dto)));

        write.MapPut("/{id:int}", async (int id, SaveTerritoryRequest request, ClaimsPrincipal user, TerritoryService service, CancellationToken ct) =>
            (await service.UpdateAsync(id, request, user.GetActorId(), ct)).ToHttp(Results.Ok));

        write.MapDelete("/{id:int}", async (int id, [FromQuery] bool? confirmAssignments, ClaimsPrincipal user, TerritoryService service, CancellationToken ct) =>
            (await service.DeleteAsync(id, confirmAssignments == true, user.GetActorId(), ct)).ToHttp(_ => Results.NoContent()));
    }

    private static void MapPublishers(RouteGroupBuilder group)
    {
        group.MapGet("/", async (PublisherService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)));

        var write = group.MapGroup(string.Empty).RequireAuthorization(AuthorizationPolicies.Manager);

        write.MapPost("/", async (SavePublisherRequest request, ClaimsPrincipal user, PublisherService service, CancellationToken ct) =>
            (await service.CreateAsync(request, user.GetActorId(), ct)).ToHttp(result => result.Reactivated
                ? Results.Ok(result)
                : Results.Created($"/api/publishers/{result.Publisher.Id}", result)));

        write.MapPut("/{id:int}", async (int id, SavePublisherRequest request, ClaimsPrincipal user, PublisherService service, CancellationToken ct) =>
            (await service.UpdateAsync(id, request, user.GetActorId(), ct)).ToHttp(Results.Ok));

        write.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, PublisherService service, CancellationToken ct) =>
            (await service.DeleteAsync(id, user.GetActorId(), ct)).ToHttp(_ => Results.NoContent()));
    }
}
