using System.Security.Claims;
using HoaSap.BLL.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace HoaSap.API.Controllers;
[ApiController, Route("api/shipping")]
public sealed class ShippingController(ShippingQuoteService service) : ControllerBase
{
    [HttpPost("quote"), Authorize]
    public Task<ShippingQuoteDto> Quote(ShippingQuoteRequest request, CancellationToken ct)
    { if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) throw new UnauthorizedAccessException(); return service.QuoteAsync(id, request, ct); }
}
