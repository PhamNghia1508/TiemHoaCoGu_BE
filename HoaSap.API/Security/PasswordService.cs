using System.Security.Cryptography;

namespace HoaSap.API.Security;

public static class PasswordService
{
    private const int Iterations = 210_000;
    public const string PolicyMessage = "Mật khẩu phải dài 10-128 ký tự và chứa chữ hoa, chữ thường, chữ số và ký tự đặc biệt.";

    public static bool IsValid(string? password) =>
        password is not null && password.Length is >= 10 and <= 128 &&
        password.Any(char.IsUpper) && password.Any(char.IsLower) &&
        password.Any(char.IsDigit) && password.Any(ch => !char.IsLetterOrDigit(ch));

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        try
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
            var rounds = int.Parse(parts[1]);
            if (rounds < 100_000 || rounds > 1_000_000) return false;
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[2]), rounds, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException) { return false; }
    }
}
