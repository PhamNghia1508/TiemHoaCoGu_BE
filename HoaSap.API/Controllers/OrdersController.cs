using System.Security.Claims;
using HoaSap.BLL.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace HoaSap.API.Controllers;

[ApiController, Route("api/orders")]
public sealed class OrdersController(CommerceService service) : ControllerBase
{ Guid? Me() { var x = User.FindFirstValue(ClaimTypes.NameIdentifier); return Guid.TryParse(x, out var id) ? id : null; } [HttpPost("checkout")] public Task<OrderDto> Checkout(CheckoutRequest r, CancellationToken ct) => service.CheckoutAsync(Me(), r, ct); [HttpGet("{id:guid}")][Authorize] public Task<OrderDto> Get(Guid id, CancellationToken ct) => service.OwnOrderAsync(Me() ?? throw new UnauthorizedAccessException(), id, ct); [HttpPost("{id:guid}/cancel")][Authorize] public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) { await service.CancelAsync(Me() ?? throw new UnauthorizedAccessException(), id, ct); return NoContent(); } [HttpGet, Authorize(Roles = "Admin,Staff")] public Task<IReadOnlyList<OrderDto>> Staff(CancellationToken ct) => service.StaffOrdersAsync(ct); [HttpPost("{id:guid}/status"), Authorize(Roles = "Admin,Staff")] public async Task<IActionResult> Status(Guid id, StatusRequest r, CancellationToken ct) { await service.TransitionAsync(id, r.Status, ct); return NoContent(); } [HttpPost("{id:guid}/deliveries"), Authorize(Roles = "Admin,Staff")] public Task<DeliveryDto> Delivery(Guid id, DeliveryRequest r, CancellationToken ct) => service.DeliveryAsync(id, r, ct); }
