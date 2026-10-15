using Territory.Infrastructure.Security;

namespace Territory.Tests;

/// <summary>Fixed key shared by every test host: EF caches the model (and its converters) per process.</summary>
public static class TestPersonalData
{
    public const string Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    public static AesGcmPersonalDataProtector Protector { get; } = AesGcmPersonalDataProtector.FromBase64(Key);
}
