using HoaSap.API.Storage;
using HoaSap.BLL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoaSap.API.Controllers;

/// <summary>Local media storage. Mount Media:StoragePath on a persistent volume in production.</summary>
[ApiController]
public sealed partial class MediaController(IWebHostEnvironment environment, IConfiguration configuration) : ControllerBase
{
    private MediaStorageOptions Options => configuration.GetSection(MediaStorageOptions.SectionName).Get<MediaStorageOptions>() ?? new();
    private int MaximumBytes => Options.MaximumBytes is >= 1 and <= (20 * 1024 * 1024) ? Options.MaximumBytes : 900 * 1024;
    private string StoragePath => Options.Resolve(environment.ContentRootPath);

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
        var format = MediaFileValidator.Identify(data);
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
        if (!MediaFileValidator.IsSafeName(fileName)) return NotFound();
        var path = Path.Combine(StoragePath, fileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        var contentType = MediaFileValidator.ContentType(fileName)!;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return PhysicalFile(path, contentType);
    }
}
