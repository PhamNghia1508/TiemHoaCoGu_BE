using System.Security.Claims;
using HoaSap.BLL;
using HoaSap.BLL.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoaSap.API.Controllers;

[ApiController, Route("api/gateway")]
public sealed class GatewayController(GatewayPaymentService payments, GatewayReconciliationService reconciliation) : ControllerBase
{
    [HttpPost("orders/{orderId:guid}/charge"), Authorize]
    public Task<CheckoutPaymentResponse> Charge(Guid orderId, GatewayPaymentRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var owner)) throw new BusinessException(401, "Yêu cầu đăng nhập.");
        request.OrderId = orderId; return payments.StartAsync(owner, request, ct);
    }

    [HttpGet("orders/{orderId:guid}/balance"), Authorize]
    public Task<OrderPaymentSummary> Balance(Guid orderId, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var owner)) throw new BusinessException(401, "Yêu cầu đăng nhập.");
        return reconciliation.BalanceAsync(orderId, owner, ct);
    }

    [HttpGet("reconciliation"), Authorize(Roles = "Admin")]
    public Task<IReadOnlyList<PaymentReconciliationDto>> ReconciliationQueue(CancellationToken ct) => reconciliation.ListAsync(ct);

    [HttpPost("reconciliation/{id:guid}"), Authorize(Roles = "Admin")]
    public Task<PaymentReconciliationDto> Reconcile(Guid id, PaymentReconciliationInput input, CancellationToken ct) => reconciliation.ReconcileAsync(id, input, ct);

    [HttpPost("webhook"), AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        using var stream = new MemoryStream(); await Request.Body.CopyToAsync(stream, ct);
        var result = await payments.WebhookAsync(stream.ToArray(), Request.Headers["X-Signature"].FirstOrDefault(), Request.Headers["X-Timestamp"].FirstOrDefault(), ct);
        return Ok(result);
    }
}
