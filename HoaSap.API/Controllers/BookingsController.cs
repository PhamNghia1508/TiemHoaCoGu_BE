using System.Security.Claims;
using HoaSap.BLL.Workshops;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace HoaSap.API.Controllers;

[ApiController, Route("api/bookings")]
public sealed class BookingsController(WorkshopService service) : ControllerBase
{
    private Guid? UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private bool Staff => User.IsInRole("Admin") || User.IsInRole("Staff");
    [HttpPost("sessions/{sessionId:guid}")] public Task<BookingDto> Hold(Guid sessionId, BookingRequest r, CancellationToken ct) => service.HoldBookingAsync(sessionId, r, UserId, ct);
    [HttpGet][Authorize] public Task<IReadOnlyList<BookingDto>> Mine(CancellationToken ct) => service.MyBookingsAsync(UserId ?? throw new HoaSap.BLL.BusinessException(401, "Yêu cầu đăng nhập."), ct);
    [HttpGet("{id:guid}")][Authorize] public Task<BookingDto> Get(Guid id, CancellationToken ct) => service.BookingAsync(id, UserId, Staff, ct);
    [HttpPost("{id:guid}/cancel")][Authorize] public Task Cancel(Guid id, [FromBody] string? reason, CancellationToken ct) => service.CancelBookingAsync(id, UserId, Staff, reason, ct);
    [HttpPost("{id:guid}/check-in"), Authorize(Roles = "Admin,Staff")] public Task CheckIn(Guid id, CancellationToken ct) => service.CheckInAsync(id, false, ct);
    [HttpPost("{id:guid}/no-show"), Authorize(Roles = "Admin,Staff")] public Task NoShow(Guid id, CancellationToken ct) => service.CheckInAsync(id, true, ct);
    [HttpPost("{id:guid}/refund"), Authorize(Roles = "Admin,Staff")] public Task Refund(Guid id, RefundRequest r, CancellationToken ct) => service.RefundAsync(id, r, UserId ?? Guid.Empty, ct);
}
