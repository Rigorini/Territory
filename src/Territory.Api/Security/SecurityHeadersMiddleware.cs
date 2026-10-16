namespace Territory.Api.Security;

/// <summary>Adds defensive HTTP headers to every response (API and SPA).</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    // 'unsafe-inline' for styles is required by Angular Material runtime styles; scripts stay 'self' only.
    public static readonly string ContentSecurityPolicy = Build("'self'");

    // Visual Studio Browser Link and dotnet watch hot reload connect to random localhost ports (HTTP and WebSocket).
    // Loopback only, and only in Development: production keeps connect-src 'self'.
    public static readonly string DevelopmentContentSecurityPolicy =
        Build("'self' http://localhost:* https://localhost:* ws://localhost:* wss://localhost:*");

    private readonly string contentSecurityPolicy = PolicyFor(environment);

    public static string PolicyFor(IHostEnvironment environment) =>
        environment.IsDevelopment() ? DevelopmentContentSecurityPolicy : ContentSecurityPolicy;

    private static string Build(string connectSources) =>
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        // Map tiles: OpenStreetMap for the street map, Esri for the optional satellite imagery.
        "img-src 'self' data: blob: https://tile.openstreetmap.org https://server.arcgisonline.com; " +
        "font-src 'self'; " +
        $"connect-src {connectSources}; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = contentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "geolocation=(self), camera=(), microphone=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers.Remove("Server");
            headers.Remove("X-Powered-By");
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                // API responses may carry personal data or share tokens: never store them in any cache.
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
