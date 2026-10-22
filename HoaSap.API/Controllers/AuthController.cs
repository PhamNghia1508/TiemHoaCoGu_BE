using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HoaSap.API.Security;
using HoaSap.BLL;
using HoaSap.DAL;
using HoaSap.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace HoaSap.API.Controllers;

[ApiController, Route("api/auth"), EnableRateLimiting("auth")]
public sealed class AuthController(HoaSapDbContext db, IConfiguration config) : ControllerBase
{
    [HttpPost("register"), AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Customers.AnyAsync(x => x.Email == email || x.PhoneNumber == request.PhoneNumber, ct))
            throw new BusinessException(409, "Email hoặc số điện thoại đã được sử dụng.");
        var customer = new Customer
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PhoneNumber = request.PhoneNumber.Trim(),
            PasswordHash = PasswordService.Hash(request.Password),
            Role = "Customer",
            IsGuest = false,
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return StatusCode(201, CreateToken(customer));
    }

    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Customers.SingleOrDefaultAsync(x => x.Email == email && !x.IsGuest, ct);
        // A hash is always computed to reduce account-enumeration timing differences.
        var valid = user is not null
            ? PasswordService.Verify(request.Password, user.PasswordHash)
            : PasswordService.Verify(request.Password, PasswordService.Hash("dummy-unusable-password"));
        if (!valid || user is null) throw new BusinessException(401, "Email hoặc mật khẩu không đúng.");
        return Ok(CreateToken(user));
    }

    [HttpPost("change-password"), Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (!PasswordService.IsValid(request.NewPassword))
            throw new BusinessException(400, PasswordService.PolicyMessage);
        if (request.NewPassword != request.ConfirmNewPassword)
            throw new BusinessException(400, "Mật khẩu mới và xác nhận mật khẩu không khớp.");

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            throw new BusinessException(401, "Phiên đăng nhập không hợp lệ.");
        var user = await db.Customers.SingleOrDefaultAsync(x => x.Id == id && !x.IsGuest, ct);
        if (user is null || !PasswordService.Verify(request.CurrentPassword, user.PasswordHash))
            throw new BusinessException(401, "Mật khẩu hiện tại không đúng.");
        if (PasswordService.Verify(request.NewPassword, user.PasswordHash))
            throw new BusinessException(400, "Mật khẩu mới phải khác mật khẩu hiện tại.");

        var previousHash = user.PasswordHash;
        var newHash = PasswordService.Hash(request.NewPassword);
        var changed = await db.Customers.Where(x => x.Id == id && x.PasswordHash == previousHash)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.PasswordHash, newHash), ct);
        if (changed != 1) throw new BusinessException(409, "Mật khẩu vừa thay đổi. Vui lòng đăng nhập lại.");
        return NoContent();
    }

    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Customers.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.FullName, x.Email, x.PhoneNumber, x.Role }).SingleOrDefaultAsync(ct);
        return user is null ? NotFound() : Ok(user);
    }

    private object CreateToken(Customer user)
    {
        var expires = DateTime.UtcNow.AddMinutes(config.GetValue("Jwt:ExpirationMinutes", 60));
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(SecurityStampService.ClaimType, SecurityStampService.Derive(user.PasswordHash, config["Jwt:Key"]!)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"], claims, expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        return new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), expiresAt = expires, user = new { user.Id, user.FullName, user.Email, user.Role } };
    }
}

public sealed record RegisterRequest(
    [Required, StringLength(120, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, RegularExpression(@"^\+?[0-9]{9,15}$")] string PhoneNumber,
    [Required, StringLength(128, MinimumLength = 10)] string Password);
public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required, StringLength(128)] string Password);
public sealed record ChangePasswordRequest(
    [Required, StringLength(128)] string CurrentPassword,
    [Required, StringLength(128, MinimumLength = 10)] string NewPassword,
    [Required, StringLength(128, MinimumLength = 10)] string ConfirmNewPassword);
