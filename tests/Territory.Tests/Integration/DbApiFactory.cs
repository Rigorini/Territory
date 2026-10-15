using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Territory.Infrastructure.Security;

namespace Territory.Tests.Integration;

/// <summary>Hosts the real API pipeline against the Testcontainers database.</summary>
public sealed class DbApiFactory(
    string connectionString, Action<IServiceCollection>? configureServices = null, bool fileLogging = false)
    : WebApplicationFactory<Program>
{
    private const int DeleteAttempts = 10;

    /// <summary>Private folder for the rolling log file (only when enabled), deleted on dispose.</summary>
    public string LogDirectory { get; } = Path.Combine(Path.GetTempPath(), "territory-tests-logs", Guid.NewGuid().ToString("N"));

    /// <summary>Reads every log file written so far; the sink keeps the file open, so it is shared for reading.</summary>
    public async Task<string> ReadLogAsync()
    {
        if (!Directory.Exists(LogDirectory))
        {
            return string.Empty;
        }

        var content = new System.Text.StringBuilder();
        foreach (var file in Directory.GetFiles(LogDirectory))
        {
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            content.Append(await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        }

        return content.ToString();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await TestDirectories.DeleteAsync(LogDirectory);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Territory", connectionString);
        builder.UseSetting(AesGcmPersonalDataProtector.ConfigurationKey, TestPersonalData.Key);
        builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore", "Warning");
        builder.UseSetting("FileLogging:Enabled", fileLogging ? "true" : "false");
        builder.UseSetting("FileLogging:Path", Path.Combine(LogDirectory, "territory-.log"));
        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}
