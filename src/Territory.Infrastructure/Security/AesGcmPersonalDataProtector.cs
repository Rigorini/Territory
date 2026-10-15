using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Territory.Application.Abstractions;

namespace Territory.Infrastructure.Security;

/// <summary>
/// AES-256-GCM with a random nonce per value. The encryption key and the blind-index key are derived
/// (HKDF-SHA256, distinct labels) from a single 256-bit master key, so they are never the same key.
/// Stored format: "enc:v1:" + base64(nonce | tag | ciphertext).
/// </summary>
public sealed class AesGcmPersonalDataProtector : IPersonalDataCipher
{
    public const string ConfigurationKey = "PersonalData:Key";
    public const string KeyFileConfigurationKey = "PersonalData:KeyFile";
    public const string DefaultKeyFile = "keys/personal-data.key";
    public const int KeySizeBytes = 32;
    public const string Prefix = "enc:v1:";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] encryptionKey;
    private readonly byte[] indexKey;

    public AesGcmPersonalDataProtector(byte[] masterKey)
    {
        if (masterKey.Length != KeySizeBytes)
        {
            throw new ArgumentException($"The master key must be {KeySizeBytes} bytes.", nameof(masterKey));
        }

        encryptionKey = Derive(masterKey, "territory/personal-data/encryption/v1");
        indexKey = Derive(masterKey, "territory/personal-data/blind-index/v1");
    }

    /// <summary>
    /// Reads the key from <c>PersonalData:Key</c> or, when absent, from the file in <c>PersonalData:KeyFile</c>
    /// (default <see cref="DefaultKeyFile"/>, relative to <paramref name="contentRoot"/>). Never creates a key: a missing key must stop the application.
    /// </summary>
    public static AesGcmPersonalDataProtector FromConfiguration(IConfiguration configuration, string contentRoot)
    {
        var inline = configuration[ConfigurationKey];
        if (!string.IsNullOrWhiteSpace(inline))
        {
            return FromBase64(inline);
        }

        var path = ResolveKeyFile(configuration, contentRoot);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"No personal data key: set '{ConfigurationKey}' or create the key file '{path}' (see README, \"Personal data encryption\").");
        }

        return FromBase64(File.ReadAllText(path), $"key file '{path}'");
    }

    public static string ResolveKeyFile(IConfiguration configuration, string contentRoot)
    {
        var configured = configuration[KeyFileConfigurationKey];
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? DefaultKeyFile : configured, contentRoot);
    }

    /// <summary>Writes a new random key to a file that must not exist yet, readable only by the owner where the OS supports it.</summary>
    public static void CreateKeyFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var writer = new StreamWriter(path, Encoding.ASCII, options);
        writer.WriteLine(GenerateKey());
    }

    /// <summary>Parses a base64 key without ever echoing its value.</summary>
    public static AesGcmPersonalDataProtector FromBase64(string? base64Key, string source = $"configuration value '{ConfigurationKey}'")
    {
        base64Key = base64Key?.Trim();
        var buffer = new byte[KeySizeBytes];
        if (string.IsNullOrWhiteSpace(base64Key)
            || !Convert.TryFromBase64String(base64Key, buffer, out var written)
            || written != KeySizeBytes)
        {
            throw new InvalidOperationException($"The {source} must be a base64-encoded {KeySizeBytes * 8}-bit key.");
        }

        return new AesGcmPersonalDataProtector(buffer);
    }

    public static string GenerateKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySizeBytes));

    public static bool IsProtected(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plainText)
    {
        var plain = Encoding.UTF8.GetBytes(plainText);
        var payload = new byte[NonceSize + TagSize + plain.Length];
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(encryptionKey, TagSize);
        aes.Encrypt(nonce, plain, payload.AsSpan(NonceSize + TagSize), tag);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedText)
    {
        if (!IsProtected(protectedText))
        {
            throw new CryptographicException("The value is not protected personal data.");
        }

        var payload = DecodePayload(protectedText[Prefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The protected value is truncated.");
        }

        var plain = new byte[payload.Length - NonceSize - TagSize];
        using var aes = new AesGcm(encryptionKey, TagSize);
        aes.Decrypt(
            payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize), plain);
        return Encoding.UTF8.GetString(plain);
    }

    public string BlindIndex(string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(indexKey, Encoding.UTF8.GetBytes(value)));

    private static byte[] DecodePayload(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("The protected value is malformed.", ex);
        }
    }

    private static byte[] Derive(byte[] masterKey, string label) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, KeySizeBytes, info: Encoding.ASCII.GetBytes(label));
}
