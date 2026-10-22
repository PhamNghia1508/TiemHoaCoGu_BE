using System.Security.Cryptography;
using System.Text;

namespace HoaSap.API.Security;

public static class SecurityStampService
{
    public const string ClaimType = "pwd_stamp";

    public static string Derive(string passwordHash, string signingKey)
    {
        var key = Encoding.UTF8.GetBytes(signingKey);
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(passwordHash));
        return Base64UrlEncode(hash);
    }

    public static bool Matches(string? presented, string passwordHash, string signingKey)
    {
        if (string.IsNullOrWhiteSpace(presented)) return false;
        var expected = Derive(passwordHash, signingKey);
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return presentedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes);
    }

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
