using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HoaSap.Tests;

public sealed class ApiFixture : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"hoasap-tests-{Guid.NewGuid():N}.db");
    private readonly int _authPermitLimit;
    public ApiFixture() : this(1000) { }
    internal ApiFixture(int authPermitLimit) => _authPermitLimit = authPermitLimit;
    public const string AdminEmail = "qa-admin@example.test";
    public const string AdminPassword = "A-very-long-test-password-123!";
    public const string TestJwtKey = "qa-only-key-with-at-least-32-bytes-123456";
    internal string? CachedAdminToken { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
            ["Media:StoragePath"] = _dbPath + "-media",
            ["Database:Initialize"] = "true",
            ["Database:Seed"] = "false",
            ["RateLimiting:AuthPermitLimit"] = _authPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Jwt:Key"] = TestJwtKey,
            ["Jwt:Issuer"] = "qa-tests",
            ["Jwt:Audience"] = "qa-tests",
            ["BootstrapAdmin:Email"] = AdminEmail,
            ["BootstrapAdmin:Password"] = AdminPassword,
            ["BootstrapAdmin:PhoneNumber"] = "0900000001",
            ["BootstrapAdmin:FullName"] = "QA Admin"
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-shm"); File.Delete(_dbPath + "-wal"); } catch { }
        try { Directory.Delete(_dbPath + "-media", true); } catch { }
    }
}

[CollectionDefinition("api", DisableParallelization = true)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture> { }

public abstract class ApiTestBase(ApiFixture fixture)
{
    protected readonly HttpClient Client = fixture.CreateClient();

    protected static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    protected async Task<string> RegisterAsync(string email, string? phone = null, string password = "Password-123!")
    {
        phone ??= "09" + Random.Shared.Next(100000000, 999999999).ToString();
        var response = await Client.PostAsync("/api/auth/register", Json(new { fullName = "QA Customer", phoneNumber = phone, email, password, role = "Admin" }));
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("accessToken").GetString()!;
    }

    protected async Task<HttpClient> AuthenticatedAsync(string token)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected async Task<string> AdminTokenAsync()
    {
        if (fixture.CachedAdminToken is { } cached) return cached;
        var response = await Client.PostAsync("/api/auth/login", Json(new { email = ApiFixture.AdminEmail, password = ApiFixture.AdminPassword }));
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        fixture.CachedAdminToken = document.RootElement.GetProperty("accessToken").GetString()!;
        return fixture.CachedAdminToken;
    }
}
