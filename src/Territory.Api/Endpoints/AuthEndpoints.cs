using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Territory.Api.Security;
using Territory.Application.Auth;
using Territory.Application.Security;

namespace Territory.Api.Endpoints;

public sealed record LoginRequest(string? Username, string? Password);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth");

        group.MapGet("/antiforgery", (HttpContext context) =>
        {
            AuthSetup.IssueXsrfToken(context);
            return Results.NoContent();
        }).AllowAnonymous().WithMetadata(AllowPendingPasswordChange.Instance);

        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Login);
        group.MapPost("/logout", (Delegate)LogoutAsync).AllowAnonymous().WithMetadata(AllowPendingPasswordChange.Instance);
        group.MapGet("/me", GetMeAsync).WithMetadata(AllowPendingPasswordChange.Instance);
        group.MapPut("/me/preferences", UpdatePreferencesAsync);
        group.MapPut("/me/password", ChangePasswordAsync).WithMetadata(AllowPendingPasswordChange.Instance);
        return api;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, HttpContext context, AuthService auth, CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request.Username, request.Password, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToHttp();
        }

        var user = result.Value!;
        var principal = AuthSetup.CreatePrincipal(user);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        context.User = principal;
        AuthSetup.IssueXsrfToken(context);
        return Results.Ok(new AdminProfile(user.Id, user.Username, user.DisplayName, user.Theme, user.Language, user.Role, user.MustChangePassword));
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, SecurityAuditLogger audit)
    {
        if (context.User.GetAdminId() is { } adminId)
        {
            audit.LoggedOut(adminId);
        }

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        AuthSetup.IssueXsrfToken(context);
        return Results.NoContent();
    }

    private static async Task<IResult> GetMeAsync(HttpContext context, AuthService auth, CancellationToken cancellationToken)
    {
        var adminId = context.User.GetAdminId();
        var profile = adminId is null ? null : await auth.GetProfileAsync(adminId.Value, cancellationToken);
        return profile is null ? Results.Unauthorized() : Results.Ok(profile);
    }

    private static async Task<IResult> UpdatePreferencesAsync(
        PreferencesRequest request, ClaimsPrincipal user, AuthService auth, CancellationToken cancellationToken)
    {
        var result = await auth.UpdatePreferencesAsync(user.GetActorId(), request, cancellationToken);
        return result.ToHttp(Results.Ok);
    }

    // The stamp rotates, so the cookie is reissued for this session; every other session is signed out.
    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request, HttpContext context, AuthService auth, CancellationToken cancellationToken)
    {
        var result = await auth.ChangePasswordAsync(context.User.GetActorId(), request, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToHttp();
        }

        var principal = AuthSetup.CreatePrincipal(result.Value!);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        context.User = principal;
        AuthSetup.IssueXsrfToken(context);
        return Results.NoContent();
    }
}
