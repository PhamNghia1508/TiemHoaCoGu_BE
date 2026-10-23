using System.Security.Claims;
using HoaSap.BLL;
using HoaSap.BLL.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoaSap.API.Controllers;

[ApiController, Route("api/refunds"), Authorize(Roles = "Admin,Staff")]
public sealed class RefundsController(RefundService service, GatewayRefundService gateway) : ControllerBase
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new BusinessException(401, "Yêu cầu đăng nhập.");

    [HttpGet]
    public Task<RefundPage> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => service.ListAsync(page, pageSize, ct);

    [HttpPost("order/{id:guid}")]
    public Task<RefundDto> RequestOrder(Guid id, RefundRequestDto request, CancellationToken ct) => service.RequestOrderAsync(id, request, ct);

    [HttpPost("{id:guid}/approve"), Authorize(Roles = "Admin")]
    public Task<RefundDto> Approve(Guid id, CancellationToken ct) => service.ApproveAsync(id, Actor, ct);

    [HttpPost("{id:guid}/reject"), Authorize(Roles = "Admin")]
    public Task<RefundDto> Reject(Guid id, CancellationToken ct) => service.RejectAsync(id, Actor, ct);

    [HttpPost("{id:guid}/process"), Authorize(Roles = "Admin")]
    public Task<RefundDto> Process(Guid id, RefundProcessDto request, CancellationToken ct) => service.ProcessAsync(id, request.TransactionCode, ct);

    [HttpPost("{id:guid}/reconcile-gateway"), Authorize(Roles = "Admin")]
    public Task<RefundDto> ReconcileGateway(Guid id, RefundProcessDto request, CancellationToken ct) => gateway.ReconcileAsync(id, request.TransactionCode, ct);

    [HttpPost("{id:guid}/process-gateway"), Authorize(Roles = "Admin")]
    public Task<RefundDto> ProcessGateway(Guid id, RefundProcessGatewayDto request, CancellationToken ct) => gateway.ProcessAsync(id, request.OriginalTransactionCode, ct);
}
