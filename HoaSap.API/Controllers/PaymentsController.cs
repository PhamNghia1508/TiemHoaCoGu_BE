using System.Security.Claims;
using HoaSap.BLL.Workshops;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace HoaSap.API.Controllers;

[ApiController, Route("api/payments"), Authorize(Roles = "Admin,Staff")]
public sealed class PaymentsController(WorkshopService service) : ControllerBase
{
    [HttpPost("booking/{bookingId:guid}")] public async Task<IActionResult> Booking(Guid bookingId, ManualPaymentRequest r, CancellationToken ct) { await service.RecordPaymentAsync(bookingId, null, r, StaffId(), ct); return Ok(new { recorded = true }); }
    [HttpPost("order/{orderId:guid}")] public async Task<IActionResult> Order(Guid orderId, ManualPaymentRequest r, CancellationToken ct) { await service.RecordPaymentAsync(null, orderId, r, StaffId(), ct); return Ok(new { recorded = true }); }
    private Guid StaffId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new HoaSap.BLL.BusinessException(401, "Yêu cầu đăng nhập staff.");
}
