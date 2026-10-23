using System.Security.Claims;
using HoaSap.BLL.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace HoaSap.API.Controllers;

[ApiController, Authorize, Route("api/cart")]
public sealed class CartController(CommerceService service) : ControllerBase
{ Guid Me() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException()); [HttpGet] public Task<IReadOnlyList<CartLineDto>> Get(CancellationToken ct) => service.CartAsync(Me(), ct); [HttpPost] public async Task<IActionResult> Add(CartItemRequest r, CancellationToken ct) { await service.AddCartAsync(Me(), r, ct); return NoContent(); } [HttpDelete("{id:guid}")] public async Task<IActionResult> Remove(Guid id, CancellationToken ct) { await service.RemoveCartAsync(Me(), id, ct); return NoContent(); } }
