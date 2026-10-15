using Serilog.Context;

namespace Territory.Api.Logging;

/// <summary>Adds the client address to every event logged while processing the request.</summary>
public sealed class RequestLogContextMiddleware(RequestDelegate next)
{
    public const string ClientIpProperty = "ClientIp";

    public async Task InvokeAsync(HttpContext context)
    {
        using (LogContext.PushProperty(ClientIpProperty, context.Connection.RemoteIpAddress?.ToString()))
        {
            await next(context);
        }
    }
}
