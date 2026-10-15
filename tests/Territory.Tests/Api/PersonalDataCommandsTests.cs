using Microsoft.Extensions.Configuration;
using Territory.Api.Cli;
using Territory.Infrastructure.Security;

namespace Territory.Tests.Api;

public sealed class PersonalDataCommandsTests
{
    private static readonly string Root = Path.GetTempPath();

    private static IConfiguration Config(string? key, string? connectionString = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AesGcmPersonalDataProtector.ConfigurationKey] = key,
                ["ConnectionStrings:Territory"] = connectionString
            })
            .Build();

    [Theory]
    [InlineData("generate-personal-data-key", true)]
    [InlineData("protect-personal-data", true)]
    [InlineData("encrypt-existing-personal-data", true)]
    [InlineData("hash-password", false)]
    [InlineData(null, false)]
    public void IsCommand_RecognisesOnlyPersonalDataCommands(string? name, bool expected) =>
        Assert.Equal(expected, PersonalDataCommands.IsCommand(name));

    [Fact]
    public void GenerateKey_PrintsAUsableKey()
    {
        var output = new StringWriter();

        Assert.Equal(0, PersonalDataCommands.GenerateKey(null, Root, output, new StringWriter()));

        Assert.NotNull(AesGcmPersonalDataProtector.FromBase64(output.ToString().Trim()));
    }

    [Fact]
    public async Task RunAsync_Generate_ReturnsZero()
    {
        var code = await PersonalDataCommands.RunAsync(
            new[] { PersonalDataCommands.GenerateKeyName }, Config(null), Root, new StringWriter(), new StringWriter(), TestContext.Current.CancellationToken);

        Assert.Equal(0, code);
    }

    [Fact]
    public void Protect_EncryptsTheTrimmedValue()
    {
        var output = new StringWriter();

        var code = PersonalDataCommands.Protect(Config(TestPersonalData.Key), Root, () => "  Mario Rossi ", output, new StringWriter());

        Assert.Equal(0, code);
        Assert.Equal("Mario Rossi", TestPersonalData.Protector.Unprotect(output.ToString().Trim()));
    }

    [Theory]
    [InlineData(null, "Mario")]
    [InlineData("%%%", "Mario")]
    [InlineData(TestPersonalData.Key, null)]
    [InlineData(TestPersonalData.Key, "   ")]
    public void Protect_FailsWithoutKeyOrValue(string? key, string? value)
    {
        var error = new StringWriter();

        var code = PersonalDataCommands.Protect(Config(key), Root, () => value, new StringWriter(), error);

        Assert.Equal(1, code);
        Assert.NotEmpty(error.ToString());
        Assert.DoesNotContain("%%%", error.ToString());
    }

    [Fact]
    public async Task EncryptExisting_FailsWithoutConnectionStringOrKey()
    {
        var noConnection = new StringWriter();
        var noKey = new StringWriter();
        var ct = TestContext.Current.CancellationToken;

        var first = await PersonalDataCommands.EncryptExistingAsync(Config(TestPersonalData.Key), Root, new StringWriter(), noConnection, ct);
        var second = await PersonalDataCommands.EncryptExistingAsync(Config(null, "Host=localhost"), Root, new StringWriter(), noKey, ct);

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.Contains("ConnectionStrings", noConnection.ToString());
        Assert.Contains("PersonalData:Key", noKey.ToString());
    }

    [Fact]
    public void GenerateKey_ToFile_WritesAUsableKeyAndNeverOverwrites()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var error = new StringWriter();

            Assert.Equal(0, PersonalDataCommands.GenerateKey("keys/k.key", dir.FullName, new StringWriter(), error));
            var written = File.ReadAllText(Path.Combine(dir.FullName, "keys", "k.key"));
            Assert.NotNull(AesGcmPersonalDataProtector.FromBase64(written));

            Assert.Equal(1, PersonalDataCommands.GenerateKey("keys/k.key", dir.FullName, new StringWriter(), error));
            Assert.Equal(written, File.ReadAllText(Path.Combine(dir.FullName, "keys", "k.key")));
            Assert.NotEmpty(error.ToString());
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void GenerateKey_FileFlag_UsesDefaultLocation()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            Assert.Equal(0, PersonalDataCommands.GenerateKey("--file", dir.FullName, new StringWriter(), new StringWriter()));
            Assert.True(File.Exists(Path.Combine(dir.FullName, "keys", "personal-data.key")));
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
