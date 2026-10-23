namespace HoaSap.API.Storage;

public sealed class MediaStorageOptions
{
    public const string SectionName = "Media";
    public string? StoragePath { get; set; }
    public int MaximumBytes { get; set; } = 900 * 1024;
    public string Resolve(string contentRoot)
    {
        var configured = string.IsNullOrWhiteSpace(StoragePath) ? Path.Combine(contentRoot, "App_Data", "media") : StoragePath;
        var full = Path.GetFullPath(configured, contentRoot);
        Directory.CreateDirectory(full);
        return full;
    }
}
