using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Territory.Application.Security;

namespace Territory.Api.Logging;

public static class LoggingSetup
{
    public static WebApplicationBuilder AddTerritoryLogging(this WebApplicationBuilder builder)
    {
        var fileOptions = FileLogOptions.Read(builder.Configuration, builder.Environment.ContentRootPath);
        var isDevelopment = builder.Environment.IsDevelopment();
        builder.Services.AddSerilog((_, logger) => Configure(logger, builder.Configuration, fileOptions, isDevelopment));
        return builder;
    }

    public static void Configure(
        LoggerConfiguration logger, IConfiguration configuration, FileLogOptions fileOptions, bool isDevelopment)
    {
        logger.ReadFrom.Configuration(configuration)
            .MinimumLevel.Override(SecurityAuditLogger.Category, LogEventLevel.Information)
            .Enrich.FromLogContext();

        // Containers collect stdout: structured JSON outside Development.
        if (isDevelopment)
        {
            logger.WriteTo.Console();
        }
        else
        {
            logger.WriteTo.Console(new RenderedCompactJsonFormatter());
        }

        if (fileOptions.Enabled)
        {
            // JSON lines escape control characters, so logged values cannot forge extra entries.
            logger.WriteTo.Logger(file => file
                .Filter.ByIncludingOnly(IsPersisted)
                .WriteTo.File(
                    new RenderedCompactJsonFormatter(),
                    fileOptions.Path,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: fileOptions.RetainedFileCountLimit,
                    fileSizeLimitBytes: fileOptions.FileSizeLimitBytes,
                    rollOnFileSizeLimit: false));
        }
    }

    /// <summary>The file keeps warnings, errors and every security audit event.</summary>
    public static bool IsPersisted(LogEvent logEvent) =>
        logEvent.Level >= LogEventLevel.Warning || IsSecurityAudit(logEvent);

    private static bool IsSecurityAudit(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var source)
        && source is ScalarValue { Value: SecurityAuditLogger.Category };
}
