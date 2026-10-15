using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Territory.Application.Abstractions;
using Territory.Application.Admins;
using Territory.Application.Assignments;
using Territory.Application.Auth;
using Territory.Application.Backup;
using Territory.Application.Domain;
using Territory.Application.Export;
using Territory.Application.Maps;
using Territory.Application.Municipalities;
using Territory.Application.Public;
using Territory.Application.Publishers;
using Territory.Application.Security;
using Territory.Application.Setup;
using Territory.Application.Territories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Territory.Infrastructure.Export;
using Territory.Infrastructure.Maps;
using Territory.Infrastructure.Persistence;
using Territory.Infrastructure.Security;

namespace Territory.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Territory";
    private const string ApplicationName = "Territory";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, string? contentRoot = null)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Missing configuration value 'ConnectionStrings:{ConnectionStringName}'.");
        }

        // Fail closed: the application does not start without a valid key (never stored in the repository or the database).
        services.AddSingleton<IPersonalDataCipher>(
            AesGcmPersonalDataProtector.FromConfiguration(configuration, contentRoot ?? AppContext.BaseDirectory));

        services.AddDbContext<TerritoryDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention());

        // Keys shared through the database so that every replica can read auth/antiforgery cookies.
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<TerritoryDbContext>();

        services.AddScoped<ITerritoryDbContext>(sp => sp.GetRequiredService<TerritoryDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher<AdminUser>, PasswordHasher<AdminUser>>();
        services.AddSingleton<SecurityAuditLogger>();
        services.AddScoped<AuthService>();
        services.AddScoped<AdminService>();
        services.AddScoped<SetupService>();
        services.AddScoped<MunicipalityService>();
        services.AddScoped<TerritoryService>();
        services.AddScoped<PublisherService>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<PublicTerritoryService>();
        services.AddScoped<RegisterExportService>();
        services.AddScoped<BackupService>();
        services.AddSingleton<IRegisterWriter, XlsxRegisterWriter>();
        services.AddSingleton<IRegisterWriter, DocxRegisterWriter>();
        services.AddSingleton(S13TemplateCatalogFactory.FromConfiguration(configuration, contentRoot ?? AppContext.BaseDirectory));
        services.AddSingleton<IS13Writer, DocxS13Writer>();
        services.AddScoped<S13ExportService>();
        services.AddHttpClient<IMapSource, GoogleMyMapsKmlSource>(client =>
            {
                client.BaseAddress = GoogleMyMapsKmlSource.BaseAddress;
                client.Timeout = GoogleMyMapsKmlSource.Timeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Territory/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = GoogleMyMapsKmlSource.MaxRedirects,
                UseCookies = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            });
        return services;
    }
}
