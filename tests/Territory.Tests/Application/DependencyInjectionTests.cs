using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Territory.Application.Maps;
using Territory.Infrastructure;
using Territory.Infrastructure.Maps;
using Territory.Infrastructure.Security;

namespace Territory.Tests.Application;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddInfrastructure_WithoutConnectionString_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AesGcmPersonalDataProtector.ConfigurationKey] = TestPersonalData.Key })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(configuration));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!")]
    [InlineData("c2hvcnQ=")]
    public void AddInfrastructure_WithoutAValidPersonalDataKey_Throws(string? key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Territory"] = "Host=localhost",
                [AesGcmPersonalDataProtector.ConfigurationKey] = key
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains(AesGcmPersonalDataProtector.ConfigurationKey, error.Message);
    }

    [Fact]
    public void PersonalDataKey_IsReadFromTheKeyFile_AndInlineKeyWins()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "keys"));
            File.WriteAllText(Path.Combine(dir.FullName, "keys", "personal-data.key"), TestPersonalData.Key + "\n");
            var empty = new ConfigurationBuilder().Build();
            var inlineOther = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [AesGcmPersonalDataProtector.ConfigurationKey] = AesGcmPersonalDataProtector.GenerateKey() })
                .Build();

            var fromFile = AesGcmPersonalDataProtector.FromConfiguration(empty, dir.FullName);
            var fromInline = AesGcmPersonalDataProtector.FromConfiguration(inlineOther, dir.FullName);

            Assert.Equal("x", fromFile.Unprotect(TestPersonalData.Protector.Protect("x")));
            Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
                () => fromInline.Unprotect(TestPersonalData.Protector.Protect("x")));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void PersonalDataKey_MissingOrInvalidFile_FailsClosedWithoutEchoingContent()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var empty = new ConfigurationBuilder().Build();
            var missing = Assert.Throws<InvalidOperationException>(() => AesGcmPersonalDataProtector.FromConfiguration(empty, dir.FullName));
            Assert.Contains("personal-data.key", missing.Message);

            File.WriteAllText(Path.Combine(dir.FullName, "custom.key"), "SECRET-%%%");
            var custom = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [AesGcmPersonalDataProtector.KeyFileConfigurationKey] = "custom.key" })
                .Build();
            var invalid = Assert.Throws<InvalidOperationException>(() => AesGcmPersonalDataProtector.FromConfiguration(custom, dir.FullName));
            Assert.DoesNotContain("SECRET", invalid.Message);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void MapSource_IsTheBoundedGoogleClient()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Territory"] = "Host=localhost", [AesGcmPersonalDataProtector.ConfigurationKey] = TestPersonalData.Key })
            .Build();
        var services = new ServiceCollection().AddInfrastructure(configuration);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var provider = services.BuildServiceProvider();

        Assert.IsType<GoogleMyMapsKmlSource>(provider.GetRequiredService<IMapSource>());
    }
}
