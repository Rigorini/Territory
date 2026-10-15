using System.Security.Cryptography;
using Territory.Infrastructure.Security;

namespace Territory.Tests.Application;

public sealed class AesGcmPersonalDataProtectorTests
{
    private static readonly AesGcmPersonalDataProtector Sut = TestPersonalData.Protector;

    [Theory]
    [InlineData("Mario")]
    [InlineData("Dell'Orto")]
    [InlineData("Šimić Ğüneş 李")]
    [InlineData("")]
    public void Protect_ThenUnprotect_RoundTrips(string value)
    {
        var protectedValue = Sut.Protect(value);

        Assert.StartsWith(AesGcmPersonalDataProtector.Prefix, protectedValue);
        Assert.Equal(value, Sut.Unprotect(protectedValue));
    }

    [Fact]
    public void Protect_DoesNotLeakThePlainText()
    {
        Assert.DoesNotContain("Mario", Sut.Protect("Mario"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Protect_UsesAFreshNonceEveryTime() =>
        Assert.NotEqual(Sut.Protect("Mario"), Sut.Protect("Mario"));

    [Fact]
    public void Unprotect_WithAnotherKey_Fails()
    {
        var other = AesGcmPersonalDataProtector.FromBase64(AesGcmPersonalDataProtector.GenerateKey());

        Assert.Throws<AuthenticationTagMismatchException>(() => other.Unprotect(Sut.Protect("Mario")));
    }

    [Fact]
    public void Unprotect_DetectsTampering()
    {
        var bytes = Convert.FromBase64String(Sut.Protect("Mario")[AesGcmPersonalDataProtector.Prefix.Length..]);
        bytes[^1] ^= 0x01;
        var tampered = AesGcmPersonalDataProtector.Prefix + Convert.ToBase64String(bytes);

        Assert.Throws<AuthenticationTagMismatchException>(() => Sut.Unprotect(tampered));
    }

    [Theory]
    [InlineData("Mario")]
    [InlineData("enc:v1:")]
    [InlineData("enc:v1:@@@")]
    [InlineData("enc:v1:AAAA")]
    public void Unprotect_RejectsPlainTextAndMalformedValues(string value) =>
        Assert.ThrowsAny<CryptographicException>(() => Sut.Unprotect(value));

    [Fact]
    public void BlindIndex_IsDeterministicKeyedAndNotTheEncryptionKey()
    {
        var other = AesGcmPersonalDataProtector.FromBase64(AesGcmPersonalDataProtector.GenerateKey());

        Assert.Equal(Sut.BlindIndex("mario"), Sut.BlindIndex("mario"));
        Assert.NotEqual(Sut.BlindIndex("mario"), Sut.BlindIndex("anna"));
        Assert.NotEqual(Sut.BlindIndex("mario"), other.BlindIndex("mario"));
        Assert.Matches("^[0-9a-f]{64}$", Sut.BlindIndex("mario"));
    }

    [Fact]
    public void Constructor_RejectsWrongKeySize() =>
        Assert.Throws<ArgumentException>(() => new AesGcmPersonalDataProtector(new byte[16]));

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("%%%")]
    [InlineData("AAAA")]
    public void FromBase64_RejectsInvalidKeys_WithoutEchoingThem(string? key)
    {
        var error = Assert.Throws<InvalidOperationException>(() => AesGcmPersonalDataProtector.FromBase64(key));

        Assert.DoesNotContain("%%%", error.Message);
    }

    [Fact]
    public void GenerateKey_ProducesAValid256BitKey() =>
        Assert.NotNull(AesGcmPersonalDataProtector.FromBase64(AesGcmPersonalDataProtector.GenerateKey()));
}
