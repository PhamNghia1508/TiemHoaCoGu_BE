using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using HoaSap.API.Middleware;
using HoaSap.API.Security;
using HoaSap.API.Services;
using HoaSap.BLL.Commerce;
using HoaSap.BLL.Workshops;
using HoaSap.DAL;
using HoaSap.Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var isDev = builder.Environment.IsDevelopment();
var key = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(key))
{
    if (!isDev) throw new InvalidOperationException("Set Jwt__Key (at least 32 bytes) outside source control.");
    key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    builder.Configuration["Jwt:Key"] = key;
}
if (Encoding.UTF8.GetByteCount(key) < 32) throw new InvalidOperationException("Jwt key must be at least 32 bytes.");
builder.Services.AddDbContext<HoaSapDbContext>(options =>
{
    var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
    var connection = builder.Configuration.GetConnectionString("DefaultConnection")!;
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)) options.UseSqlServer(connection);
    else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) options.UseSqlite(connection);
    else throw new InvalidOperationException("Unsupported Database:Provider");
});
builder.Services.AddScoped<CommerceService>();
builder.Services.AddScoped<WorkshopService>();
builder.Services.AddHostedService<BookingExpiryWorker>();
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Tiệm Hoa Có Gu API", Version = "v1", Description = "E-commerce và workshop. Thanh toán hiện chỉ ghi nhận thủ công bởi nhân viên; chưa kết nối cổng thanh toán." });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>() });
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) { context.Fail("Invalid subject"); return; }
            var db = context.HttpContext.RequestServices.GetRequiredService<HoaSapDbContext>();
            var user = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsGuest);
            if (user is null || user.Role != context.Principal?.FindFirstValue(ClaimTypes.Role)) context.Fail("Account or role no longer valid");
        }
    };
});
builder.Services.AddAuthorization();
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => { if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod(); }));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseMiddleware<ExceptionMiddleware>();
if (isDev) { app.UseSwagger(); app.UseSwaggerUI(); }
else { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
    if (builder.Configuration.GetValue<bool>("Database:Initialize")) await db.Database.EnsureCreatedAsync();
    if (builder.Configuration.GetValue<bool>("Database:Seed")) await DataSeeder.SeedAsync(db);
    var adminEmail = builder.Configuration["BootstrapAdmin:Email"]?.Trim().ToLowerInvariant();
    var adminPassword = builder.Configuration["BootstrapAdmin:Password"];
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        if (adminPassword.Length < 12) throw new InvalidOperationException("Bootstrap admin password must have at least 12 characters.");
        if (!await db.Customers.AnyAsync(x => x.Email == adminEmail))
        {
            var phone = builder.Configuration["BootstrapAdmin:PhoneNumber"];
            if (string.IsNullOrWhiteSpace(phone)) throw new InvalidOperationException("Bootstrap admin phone is required.");
            db.Customers.Add(new Customer { Email = adminEmail, FullName = builder.Configuration["BootstrapAdmin:FullName"] ?? "Admin", PhoneNumber = phone, PasswordHash = PasswordService.Hash(adminPassword), Role = "Admin", IsGuest = false, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
    }
}
await app.RunAsync();
public partial class Program { }
