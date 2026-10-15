using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Territory.Api.Logging;
using Territory.Application.Auth;
using Territory.Application.Domain;
using Territory.Application.Security;

namespace Territory.Api.Security;

public static class AuthSetup
{
    public const string AuthCookieName = "__Host-territory";
    public const string AntiforgeryCookieName = "__Host-af";
    public const string XsrfCookieName = "__Host-XSRF-TOKEN";
    public const string XsrfHeaderName = "X-XSRF-TOKEN";
    public const string SecurityStampClaim = "stamp";
    public const string MustChangePasswordClaim = "pwdchange";
    public static readonly TimeSpan SessionDuration = TimeSpan.FromHours(8);

    public static IServiceCollection AddTerritoryAuth(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = AuthCookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.Path = "/";
                o.ExpireTimeSpan = SessionDuration;
                o.SlidingExpiration = true;
                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx => WriteStatus(ctx.Response, StatusCodes.Status401Unauthorized),
                    OnRedirectToAccessDenied = ctx =>
                    {
                        HttpSecurityAudit.AccessDenied(ctx.HttpContext);
                        return WriteStatus(ctx.Response, StatusCodes.Status403Forbidden);
                    },
                    OnValidatePrincipal = ValidatePrincipalAsync
                };
            });
        services.AddAuthorization(AuthorizationPolicies.Register);

        services.AddAntiforgery(o =>
        {
            o.Cookie.Name = AntiforgeryCookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.Path = "/";
            o.HeaderName = XsrfHeaderName;
            o.SuppressXFrameOptionsHeader = true;
        });
        return services;
    }

    public static ClaimsPrincipal CreatePrincipal(AdminUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(SecurityStampClaim, user.SecurityStamp.ToString())
        };
        if (user.MustChangePassword)
        {
            claims.Add(new Claim(MustChangePasswordClaim, bool.TrueString));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static bool MustChangePassword(this ClaimsPrincipal principal) => principal.HasClaim(MustChangePasswordClaim, bool.TrueString);

    public static int? GetAdminId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    /// <summary>Id of the admin acting on an endpoint that requires authorization, where the claim is always present.</summary>
    public static int GetActorId(this ClaimsPrincipal principal) =>
        principal.GetAdminId() ?? throw new InvalidOperationException("Authenticated principal without admin id.");

    /// <summary>
    /// Issues a fresh request token (bound to the current <see cref="HttpContext.User"/>) in a cookie readable by the SPA.
    /// </summary>
    public static void IssueXsrfToken(HttpContext context)
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(XsrfCookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true
        });
    }

    // Rejects sessions of deleted admins or issued before a password change (security stamp mismatch).
    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var adminId = principal?.GetAdminId();
        var stampValid = Guid.TryParse(principal?.FindFirstValue(SecurityStampClaim), out var stamp);
        var services = context.HttpContext.RequestServices;
        if (adminId is not null && stampValid)
        {
            var auth = services.GetRequiredService<AuthService>();
            if (await auth.GetSessionAsync(adminId.Value, stamp, context.HttpContext.RequestAborted) is { } session)
            {
                RefreshSessionClaims(context, session);
                return;
            }
        }

        var reason = adminId is not null && stampValid ? SecurityAuditLogger.ReasonRevoked : SecurityAuditLogger.ReasonInvalidClaims;
        services.GetRequiredService<SecurityAuditLogger>().SessionRejected(reason, adminId);
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // The database is the source of truth: a role changed by an administrator applies to the open session at once.
    private static void RefreshSessionClaims(CookieValidatePrincipalContext context, SessionState session)
    {
        var principal = context.Principal!;
        if (principal.FindFirstValue(ClaimTypes.Role) == session.Role.ToString()
            && principal.MustChangePassword() == session.MustChangePassword)
        {
            return;
        }

        var claims = principal.Claims
            .Where(c => c.Type != ClaimTypes.Role && c.Type != MustChangePasswordClaim)
            .Append(new Claim(ClaimTypes.Role, session.Role.ToString()));
        if (session.MustChangePassword)
        {
            claims = claims.Append(new Claim(MustChangePasswordClaim, bool.TrueString));
        }

        context.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        context.ShouldRenew = true;
    }

    private static Task WriteStatus(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
