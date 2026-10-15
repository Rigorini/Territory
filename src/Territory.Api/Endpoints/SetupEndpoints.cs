using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Territory.Api.Security;
using Territory.Application.Admins;
using Territory.Application.Setup;

namespace Territory.Api.Endpoints;

public sealed record SetupStatus(bool Required);

public static class SetupEndpoints
{
    public static RouteGroupBuilder MapSetupEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/setup");

        group.MapGet("/status", async (SetupService setup, CancellationToken ct) =>
            Results.Ok(new SetupStatus(await setup.IsRequiredAsync(ct)))).AllowAnonymous();

        group.MapPost("", CreateFirstAdminAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Login);
        return api;
    }

    private static async Task<IResult> CreateFirstAdminAsync(
        SaveAdminRequest request, SetupService setup, IKeyManager keys, TimeProvider clock, CancellationToken ct)
    {
        var result = await setup.CreateFirstAdminAsync(request, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToHttp();
        }

        EnsureDataProtectionKey(keys, clock);
        return Results.Ok(result.Value);
    }

    // Keys are normally created lazily on first use; creating one now makes the installation ready for the first sign-in.
    private static void EnsureDataProtectionKey(IKeyManager keys, TimeProvider clock)
    {
        if (keys.GetAllKeys().Count == 0)
        {
            var now = clock.GetUtcNow();
            keys.CreateNewKey(now, now.AddDays(90));
        }
    }
}
