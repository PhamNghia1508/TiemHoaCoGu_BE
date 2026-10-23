using System.Text.RegularExpressions;

namespace HoaSap.API.Storage;

public static partial class MediaFileValidator
{
    public static bool IsSafeName(string name) => SafeFileName().IsMatch(name);
    public static string? ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    { ".png" => "image/png", ".jpg" => "image/jpeg", ".webp" => "image/webp", _ => null };
    public static (string Extension, string ContentType)? Identify(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) && data.Slice(12,4).SequenceEqual("IHDR"u8)) return (".png", "image/png");
        if (data.Length >= 4 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff && data[^2] == 0xff && data[^1] == 0xd9) return (".jpg", "image/jpeg");
        if (data.Length >= 16 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8,4).SequenceEqual("WEBP"u8) && (data.Slice(12,4).SequenceEqual("VP8 "u8) || data.Slice(12,4).SequenceEqual("VP8L"u8) || data.Slice(12,4).SequenceEqual("VP8X"u8))) return (".webp", "image/webp");
        return null;
    }
    [GeneratedRegex(@"\A[a-f0-9]{32}\.(png|jpg|webp)\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeFileName();
}
