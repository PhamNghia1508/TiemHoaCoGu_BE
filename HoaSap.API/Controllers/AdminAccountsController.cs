using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using HoaSap.API.Security;
using HoaSap.BLL;
using HoaSap.DAL;
using HoaSap.Domain;
using HoaSap.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HoaSap.API.Controllers;

[ApiController, Route("api/management"), Authorize(Roles = "Admin")]
public sealed class AdminAccountsController(HoaSapDbContext db) : ControllerBase
{
    [HttpGet("accounts")]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? role, [FromQuery] bool? isActive,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        ValidatePaging(page, pageSize);
        if (role is not null && role is not ("Admin" or "Staff" or "Customer")) throw new BusinessException(400, "Role không hợp lệ.");
        var query = db.Customers.AsNoTracking().Where(x => !x.IsGuest);
        if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim(); query = query.Where(x => x.FullName.Contains(s) || x.Email.Contains(s) || x.PhoneNumber.Contains(s)); }
        if (role is not null) query = query.Where(x => x.Role == role);
        if (isActive is not null) query = query.Where(x => x.IsActive == isActive.Value);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AccountDto(x.Id, x.FullName, x.Email, x.PhoneNumber, x.Role, x.IsActive, x.IsGuest, x.CreatedAt)).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost("accounts")]
    public async Task<IActionResult> Create(AccountCreateRequest request, CancellationToken ct)
    {
        ValidateRole(request.Role);
        ValidatePassword(request.Password);
        var email = NormalizeEmail(request.Email); var phone = request.PhoneNumber.Trim();
        ValidateIdentity(request.FullName, email, phone);
        if (await db.Customers.AnyAsync(x => x.Email == email || x.PhoneNumber == phone, ct)) throw new BusinessException(409, "Email hoặc số điện thoại đã được sử dụng.");
        var account = new Customer { FullName = request.FullName.Trim(), Email = email, PhoneNumber = phone, PasswordHash = PasswordService.Hash(request.Password), Role = request.Role, IsGuest = false, IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Customers.Add(account); await db.SaveChangesAsync(ct);
        return Created($"/api/management/accounts/{account.Id}", ToDto(account));
    }

    [HttpPut("accounts/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, AccountEditRequest request, CancellationToken ct)
    {
        ValidateRole(request.Role); var actorId = CurrentActorId();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var account = await db.Customers.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException(404, "Account not found");
        if (account.IsGuest) throw new BusinessException(400, "Guest account cannot be managed.");
        if (actorId == id && (!request.IsActive || request.Role != "Admin")) throw new BusinessException(400, "Không thể tự khóa hoặc hạ quyền tài khoản hiện tại.");
        var email = NormalizeEmail(request.Email); var phone = request.PhoneNumber.Trim(); ValidateIdentity(request.FullName, email, phone);
        if (await db.Customers.AnyAsync(x => x.Id != id && (x.Email == email || x.PhoneNumber == phone), ct)) throw new BusinessException(409, "Email hoặc số điện thoại đã được sử dụng.");
        if (account.Role == "Admin" && account.IsActive && (request.Role != "Admin" || !request.IsActive) && await db.Customers.CountAsync(x => x.Role == "Admin" && x.IsActive, ct) <= 1)
            throw new BusinessException(409, "Không thể vô hiệu hóa hoặc hạ quyền Admin cuối cùng.");
        if (account.Role != request.Role || account.IsActive != request.IsActive || account.Email != email || account.PhoneNumber != phone) await RevokeSessionsAsync(id, ct);
        account.FullName = request.FullName.Trim(); account.Email = email; account.PhoneNumber = phone; account.Role = request.Role; account.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(ToDto(account));
    }

    [HttpPost("accounts/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        ValidatePassword(request.NewPassword);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var account = await db.Customers.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException(404, "Account not found");
        if (account.IsGuest) throw new BusinessException(400, "Guest account cannot be managed.");
        account.PasswordHash = PasswordService.Hash(request.NewPassword);
        await RevokeSessionsAsync(id, ct);
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(Customer), EntityId = id.ToString(), Action = "PasswordReset", OldValuesJson = "{}", NewValuesJson = "{\"passwordReset\":true}", OccurredAtUtc = DateTime.UtcNow, ActorId = CurrentActorId(), ActorRole = User.FindFirstValue(ClaimTypes.Role), TraceId = HttpContext.TraceIdentifier });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return NoContent();
    }

    private async Task RevokeSessionsAsync(Guid customerId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await db.Set<RefreshToken>().Where(x => x.CustomerId == customerId && x.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now).SetProperty(x => x.RevocationReason, "admin-account-change"), ct);
        await db.Set<PasswordResetToken>().Where(x => x.CustomerId == customerId && x.UsedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAtUtc, now), ct);
    }

    [HttpGet("permissions")]
    public IActionResult Permissions() => Ok(new
    {
        roles = new[] {
            new { role = "Admin", description = "Toàn quyền quản trị CMS và vận hành." },
            new { role = "Staff", description = "Quyền vận hành hiện có; không có quyền CMS tài khoản." },
            new { role = "Customer", description = "Quyền mua hàng và quản lý dữ liệu cá nhân." }
        },
        cms = new { accounts = "Admin", catalog = "Admin", vouchers = "Admin", orders = "Admin", dashboard = "Admin" }
    });

    private static AccountDto ToDto(Customer x) => new(x.Id, x.FullName, x.Email, x.PhoneNumber, x.Role, x.IsActive, x.IsGuest, x.CreatedAt);
    private static void ValidatePaging(int page, int pageSize) { if (page < 1 || pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > int.MaxValue) throw new BusinessException(400, "Paging không hợp lệ."); }
    private static void ValidateRole(string role) { if (role is not ("Admin" or "Staff" or "Customer")) throw new BusinessException(400, "Role không hợp lệ."); }
    private static void ValidatePassword(string password) { if (!PasswordService.IsValid(password)) throw new BusinessException(400, PasswordService.PolicyMessage); }
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    private static void ValidateIdentity(string fullName, string email, string phone) { if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > 120 || !new EmailAddressAttribute().IsValid(email) ||  !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[0-9]{9,15}$")) throw new BusinessException(400, "Thông tin tài khoản không hợp lệ."); }
    private Guid CurrentActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new BusinessException(401, "Phiên đăng nhập không hợp lệ.");
}

public sealed record AccountDto(Guid Id, string FullName, string Email, string PhoneNumber, string Role, bool IsActive, bool IsGuest, DateTime CreatedAt);
public sealed class AccountCreateRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, RegularExpression(@"^\+?[0-9]{9,15}$")] public string PhoneNumber { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 10)] public string Password { get; set; } = "";
    [Required] public string Role { get; set; } = "Customer";
}
public sealed class AccountEditRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, RegularExpression(@"^\+?[0-9]{9,15}$")] public string PhoneNumber { get; set; } = "";
    [Required] public string Role { get; set; } = "Customer";
    public bool IsActive { get; set; } = true;
}
public sealed class ResetPasswordRequest
{
    [Required, StringLength(128, MinimumLength = 10)] public string NewPassword { get; set; } = "";
}
