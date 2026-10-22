using System.Text.RegularExpressions;
using HoaSap.BLL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoaSap.API.Controllers;

/// <summary>Local media storage. Mount Media:StoragePath on a persistent volume in production.</summary>
[ApiController]
public sealed partial class MediaController(IWebHostEnvironment environment, IConfiguration configuration) : ControllerBase
{
    private const int MaximumBytes = 900 * 1024;
    private string StoragePath => Path.GetFullPath(configuration["Media:StoragePath"]
        ?? Path.Combine(environment.ContentRootPath, "App_Data", "media"));

    [Authorize(Roles = "Admin")]
    [HttpPost("api/management/media")]
    [RequestSizeLimit(1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0 || file.Length > MaximumBytes)
            throw new BusinessException(400, "Chọn ảnh PNG, JPEG hoặc WebP không quá 900 KiB.");
        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaximumBytes) throw new BusinessException(400, "Ảnh vượt quá 900 KiB.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        var data = buffer.ToArray();
        var format = Identify(data);
        if (format is null) throw new BusinessException(400, "Chỉ chấp nhận ảnh PNG, JPEG hoặc WebP có chữ ký định dạng hợp lệ; không nhận SVG.");
        var fileName = Guid.NewGuid().ToString("N") + format.Value.Extension;
        Directory.CreateDirectory(StoragePath);
        var path = Path.Combine(StoragePath, fileName);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true);
            await output.WriteAsync(data, ct);
        }
        catch
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            throw;
        }
        return StatusCode(201, new { url = "/api/media/" + fileName, fileName, contentType = format.Value.ContentType, size = data.Length });
    }

    [AllowAnonymous]
    [HttpGet("api/media/{fileName}")]
    public IActionResult Read(string fileName)
    {
        if (!SafeFileName().IsMatch(fileName)) return NotFound();
        var path = Path.Combine(StoragePath, fileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        var contentType = Path.GetExtension(fileName) switch { ".png" => "image/png", ".jpg" => "image/jpeg", _ => "image/webp" };
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return PhysicalFile(path, contentType);
    }

    private static (string Extension, string ContentType)? Identify(byte[] bytes)
    {
        ReadOnlySpan<byte> data = bytes;
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] {137, 80, 78, 71, 13, 10, 26, 10})
            && data.Slice(12, 4).SequenceEqual("IHDR"u8)) return (".png", "image/png");
        if (data.Length >= 4 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff
            && data[^2] == 0xff && data[^1] == 0xd9) return (".jpg", "image/jpeg");
        if (data.Length >= 16 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)
            && (data.Slice(12, 4).SequenceEqual("VP8 "u8) || data.Slice(12, 4).SequenceEqual("VP8L"u8) || data.Slice(12, 4).SequenceEqual("VP8X"u8)))
            return (".webp", "image/webp");
        return null;
    }

    [GeneratedRegex(@"\A[a-f0-9]{32}\.(png|jpg|webp)\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeFileName();
}
