using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using Territory.Api.Logging;
using Territory.Application.Security;

namespace Territory.Tests.Api;

public sealed class LoggingSetupTests : IAsyncDisposable
{
    private static readonly string ContentRoot = Path.GetTempPath();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "territory-logging-tests", Guid.NewGuid().ToString("N"));

    public async ValueTask DisposeAsync() => await TestDirectories.DeleteAsync(directory);

    [Theory]
    [InlineData(LogEventLevel.Warning, "Territory.Application.Maps.Something", true)]
    [InlineData(LogEventLevel.Error, "Microsoft.AspNetCore.Diagnostics", true)]
    [InlineData(LogEventLevel.Fatal, null, true)]
    [InlineData(LogEventLevel.Information, SecurityAuditLogger.Category, true)]
    [InlineData(LogEventLevel.Information, "Territory.Application.Assignments.AssignmentService", false)]
    [InlineData(LogEventLevel.Debug, "Microsoft.EntityFrameworkCore", false)]
    [InlineData(LogEventLevel.Information, null, false)]
    public void IsPersisted_KeepsWarningsErrorsAndAuditOnly(LogEventLevel level, string? sourceContext, bool expected)
    {
        Assert.Equal(expected, LoggingSetup.IsPersisted(Event(level, sourceContext)));
    }

    [Fact]
    public void Read_WithoutConfiguration_UsesDailyRetentionOf60Files()
    {
        var options = FileLogOptions.Read(Config(), ContentRoot);

        Assert.True(options.Enabled);
        Assert.Equal(60, options.RetainedFileCountLimit);
        Assert.Equal(10L * 1024 * 1024, options.FileSizeLimitBytes);
        Assert.Equal(Path.GetFullPath(Path.Combine(ContentRoot, "logs", "territory-.log")), options.Path);
    }

    [Fact]
    public void Read_WithAbsolutePath_KeepsIt()
    {
        var path = Path.Combine(directory, "app-.log");

        var options = FileLogOptions.Read(Config(("Path", path), ("Enabled", "false")), ContentRoot);

        Assert.Equal(path, options.Path);
        Assert.False(options.Enabled);
    }

    [Theory]
    [InlineData("Path", " ")]
    [InlineData("RetainedFileCountLimit", "0")]
    [InlineData("RetainedFileCountLimit", "367")]
    [InlineData("FileSizeLimitMegabytes", "0")]
    [InlineData("FileSizeLimitMegabytes", "1025")]
    public void Read_WithOutOfRangeValue_FailsAtStartup(string key, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => FileLogOptions.Read(Config((key, value)), ContentRoot));

        Assert.Contains($"{FileLogOptions.SectionName}:{key}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configure_WritesOnlyPersistedEventsToDailyFile_AsEscapedJson()
    {
        var options = FileLogOptions.Read(Config(("Path", Path.Combine(directory, "territory-.log"))), ContentRoot);
        var configuration = new LoggerConfiguration();
        LoggingSetup.Configure(configuration, Config(), options, isDevelopment: false);

        using (var logger = configuration.CreateLogger())
        {
            logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, SecurityAuditLogger.Category)
                .Information("Audit {Value}", "line1\nforged");
            logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "Territory.Other").Information("Business info");
            logger.Warning("Something odd");
        }

        var file = Assert.Single(Directory.GetFiles(directory));
        Assert.Matches(@"territory-\d{8}\.log$", file);
        var lines = File.ReadAllLines(file);
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, l => l.Contains(SecurityAuditLogger.Category, StringComparison.Ordinal)
            && l.Contains(@"line1\nforged", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Something odd", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("Business info", StringComparison.Ordinal));
    }

    [Fact]
    public void Configure_WithFileDisabled_DoesNotCreateFiles()
    {
        var options = FileLogOptions.Read(
            Config(("Path", Path.Combine(directory, "territory-.log")), ("Enabled", "false")), ContentRoot);
        var configuration = new LoggerConfiguration();
        LoggingSetup.Configure(configuration, Config(), options, isDevelopment: true);

        using (var logger = configuration.CreateLogger())
        {
            logger.Error("Not persisted");
        }

        Assert.False(Directory.Exists(directory));
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v =>
                new KeyValuePair<string, string?>($"{FileLogOptions.SectionName}:{v.Key}", v.Value)))
            .Build();

    private static LogEvent Event(LogEventLevel level, string? sourceContext)
    {
        var properties = sourceContext is null
            ? []
            : new[] { new LogEventProperty(Serilog.Core.Constants.SourceContextPropertyName, new ScalarValue(sourceContext)) };
        return new LogEvent(DateTimeOffset.UtcNow, level, null, new MessageTemplate("m", []), properties);
    }
}
