namespace Territory.Application.Abstractions;

/// <summary>Encrypts personal data (names) at rest and derives searchable, non-reversible indexes from it.</summary>
public interface IPersonalDataCipher
{
    /// <summary>Authenticated encryption; the result is different on every call for the same input.</summary>
    string Protect(string plainText);

    /// <summary>Throws <see cref="System.Security.Cryptography.CryptographicException"/> if the value is not valid ciphertext for the configured key.</summary>
    string Unprotect(string protectedText);

    /// <summary>Deterministic keyed hash (HMAC) used to enforce uniqueness without storing the plain text.</summary>
    string BlindIndex(string value);
}
