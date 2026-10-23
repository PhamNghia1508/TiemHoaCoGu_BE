using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using HoaSap.DAL;
using HoaSap.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HoaSap.API.Controllers;

[ApiController, Route("api/addresses"), Authorize]
public sealed class AddressesController(HoaSapDbContext db) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await db.Addresses.AsNoTracking().Where(x => x.CustomerId == UserId).ToListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create(AddressRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (request.IsDefault) await db.Addresses.Where(x => x.CustomerId == UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);
        var address = new Address { CustomerId = UserId };
        Apply(address, request);
        db.Addresses.Add(address); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return StatusCode(201, address);
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, AddressRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var address = await db.Addresses.SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == UserId, ct);
        if (address is null) return NotFound();
        if (request.IsDefault) await db.Addresses.Where(x => x.CustomerId == UserId && x.Id != id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);
        Apply(address, request); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(address);
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var count = await db.Addresses.Where(x => x.Id == id && x.CustomerId == UserId).ExecuteDeleteAsync(ct);
        return count == 0 ? NotFound() : NoContent();
    }
    private static void Apply(Address a, AddressRequest r)
    {
        a.ReceiverName = r.ReceiverName.Trim(); a.ReceiverPhone = r.ReceiverPhone.Trim(); a.DetailAddress = r.DetailAddress.Trim();
        a.Ward = r.Ward.Trim(); a.District = r.District.Trim(); a.Province = r.Province.Trim(); a.IsDefault = r.IsDefault;
    }
}
public sealed record AddressRequest(
    [Required, StringLength(120)] string ReceiverName,
    [Required, RegularExpression(@"^\+?[0-9]{9,15}$")] string ReceiverPhone,
    [Required, StringLength(300)] string DetailAddress,
    [Required, StringLength(120)] string Ward,
    [Required, StringLength(120)] string District,
    [Required, StringLength(120)] string Province,
    bool IsDefault);
