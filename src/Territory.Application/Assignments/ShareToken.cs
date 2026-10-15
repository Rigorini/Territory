using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Territory.Application.Assignments;

/// <summary>A share token in clear (returned once to the admin) and its SHA-256 hash (the only stored form).</summary>
public sealed record IssuedToken(string Token, byte[] Hash);

/// <summary>Unpredictable 256-bit share tokens, Base64Url encoded (43 characters).</summary>
public static class ShareToken
{
    private const int ByteLength = 32;
    public const int TokenLength = 43;

    public static IssuedToken Issue()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength));
        return new IssuedToken(token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));

    /// <summary>Cheap shape check before any database lookup.</summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: TokenLength } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
