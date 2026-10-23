using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HoaSap.DAL;
using HoaSap.Domain;
using HoaSap.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HoaSap.BLL.Commerce;

public sealed record CheckoutPaymentResponse(Guid OrderId, decimal Amount, string TransactionCode, string? RedirectUrl);
public sealed record GatewayWebhookResponse(bool Accepted, bool Replay, Guid? OrderId);
public sealed class GatewayPaymentRequest
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string ReturnUrl { get; set; } = "";
    public string? IdempotencyKey { get; set; }
}
public sealed class GatewayWebhookRequest
{
    public Guid OrderId { get; set; }
    public string TransactionCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Status { get; set; } = "";
}

public sealed class GatewayPaymentService(HoaSapDbContext db, IPaymentGateway gateway, IOptions<CommerceGatewayOptions> options)
{
    public async Task<CheckoutPaymentResponse> StartAsync(Guid ownerId, GatewayPaymentRequest request, CancellationToken ct = default)
    {
        if (request is null || request.Amount <= 0 || decimal.Round(request.Amount, 2) != request.Amount) throw new BusinessException(400, "Số tiền không hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100) throw new BusinessException(400, "IdempotencyKey bắt buộc, tối đa 100 ký tự.");
        if (!Uri.TryCreate(request.ReturnUrl, UriKind.Absolute, out var returnUri) || returnUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(returnUri.UserInfo)) throw new BusinessException(400, "ReturnUrl phải là HTTPS.");
        var cfg = options.Value;
        try { cfg.Validate(); } catch (InvalidOperationException) { throw new BusinessException(503, "Cổng thanh toán chưa được cấu hình hợp lệ."); }
        if (!cfg.IsAllowedReturnOrigin(returnUri)) throw new BusinessException(400, "ReturnUrl không nằm trong allowlist.");
        var key = request.IdempotencyKey.Trim();
        GatewayPaymentIntent intent;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == request.OrderId && x.CustomerId == ownerId, ct) ?? throw new BusinessException(404, "Không tìm thấy đơn.");
            var existing = await db.GatewayPaymentIntents.SingleOrDefaultAsync(x => x.OrderId == order.Id && x.ClientKey == key, ct);
            if (existing is not null)
            {
                if (existing.Amount != request.Amount || existing.ReturnUrlOrigin != request.ReturnUrl) throw new BusinessException(409, "IdempotencyKey đã dùng cho nội dung khác.");
                if (existing.Status is "Pending" or "Unknown" || string.IsNullOrEmpty(existing.TransactionCode)) throw new BusinessException(409, "Giao dịch đang xử lý hoặc cần đối soát; không gửi lại yêu cầu thu tiền.");
                if (existing.Status is not ("Accepted" or "Paid")) throw new BusinessException(409, "Giao dịch đã kết thúc; không tái sử dụng mã này.");
                await tx.CommitAsync(ct);
                return new(order.Id, existing.Amount, existing.TransactionCode, existing.RedirectUrl);
            }
            if (order.PaymentStatus is not ("Unpaid" or "PartiallyPaid") || order.OrderStatus is not ("Confirmed" or "Processing" or "Delivering")) throw new BusinessException(409, "Đơn không còn nhận thanh toán.");
            if (await db.GatewayPaymentIntents.AnyAsync(x => x.OrderId == order.Id && (x.Status == "Pending" || x.Status == "Unknown" || x.Status == "Accepted" || x.ReconciliationStatus == "ReconciliationRequired"), ct)) throw new BusinessException(409, "Đơn có giao dịch chưa hoàn tất hoặc cần đối soát.");
            var paid = await PaidAsync(order.Id, ct);
            if (request.Amount > order.TotalAmount - paid) throw new BusinessException(400, "Số tiền vượt phần còn lại.");
            var custom = await db.Set<CustomOrderCheckout>().SingleOrDefaultAsync(x => x.OrderId == order.Id, ct);
            if (custom is not null && paid == 0)
            {
                var deposit = await db.Set<CustomQuotationRevision>().Where(x => x.CustomQuotationId == custom.CustomQuotationId && x.Version == custom.QuotationVersion).Select(x => x.DepositAmount).SingleAsync(ct);
                if (request.Amount < deposit) throw new BusinessException(400, "Khoản thanh toán đầu tiên phải đủ tiền cọc đã duyệt.");
            }
            intent = new GatewayPaymentIntent { OrderId = order.Id, OwnerId = ownerId, ClientKey = key, Amount = request.Amount, Currency = "VND", ReturnUrlOrigin = request.ReturnUrl, ProviderKey = "intent:" + Guid.NewGuid().ToString("N"), Status = "Pending" };
            db.Add(intent);
            order.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        GatewayChargeResult result;
        try
        {
            result = await gateway.ChargeAsync(new GatewayChargeRequest(intent.OrderId, intent.Amount, intent.Currency, request.ReturnUrl, intent.ProviderKey), ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            intent.Status = "Unknown"; intent.ReconciliationStatus = "ReconciliationRequired"; intent.ProviderResponse = "Provider outcome unknown"; intent.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw new BusinessException(503, "Chưa xác định được kết quả cổng thanh toán. Cần đối soát, không thử thu tiền lần nữa.");
        }
        if (!result.Accepted)
        {
            intent.Status = "Rejected"; intent.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct);
            throw new BusinessException(502, "Cổng thanh toán từ chối yêu cầu.");
        }
        var code = result.TransactionCode?.Trim() ?? "";
        var redirect = result.RedirectUrl ?? "";
        var validRedirect = redirect.Length == 0 || Uri.TryCreate(redirect, UriKind.Absolute, out var redirectUri) && redirectUri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(redirectUri.UserInfo);
        if (code.Length is 0 or > 100 || redirect.Length > 2048 || !validRedirect)
        {
            intent.Status = "Unknown"; intent.ReconciliationStatus = "ReconciliationRequired"; await db.SaveChangesAsync(ct);
            throw new BusinessException(502, "Phản hồi thanh toán không hợp lệ; cần đối soát.");
        }
        intent.TransactionCode = code; intent.RedirectUrl = redirect; intent.ProviderResponse = ""; intent.Status = "Accepted"; intent.UpdatedAtUtc = DateTime.UtcNow;
        db.Payments.Add(new Payment { OrderId = intent.OrderId, TransactionCode = code, PaymentType = "Gateway", PaymentMethod = "GenericSignedGateway", Amount = intent.Amount, PaymentStatus = "Pending" });
        await db.SaveChangesAsync(ct);
        return new(intent.OrderId, intent.Amount, code, redirect);
    }

    public async Task<GatewayWebhookResponse> WebhookAsync(ReadOnlyMemory<byte> raw, string? signature, string? timestamp, CancellationToken ct = default)
    {
        if (!gateway.VerifyWebhook(raw.Span, signature, timestamp)) throw new BusinessException(401, "Chữ ký webhook không hợp lệ.");
        GatewayWebhookRequest payload;
        try { payload = JsonSerializer.Deserialize<GatewayWebhookRequest>(raw.Span, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new JsonException(); }
        catch (JsonException) { throw new BusinessException(400, "Webhook không hợp lệ."); }
        var code = payload.TransactionCode?.Trim() ?? "";
        if (code.Length is 0 or > 100 || payload.Amount <= 0 || decimal.Round(payload.Amount, 2) != payload.Amount || payload.Currency != "VND") throw new BusinessException(400, "Tiền tệ hoặc số tiền webhook không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var intent = await db.GatewayPaymentIntents.SingleOrDefaultAsync(x => x.TransactionCode == code, ct) ?? throw new BusinessException(404, "Không tìm thấy giao dịch đã khởi tạo.");
        if (intent.OrderId != payload.OrderId || intent.Amount != payload.Amount || intent.Currency != payload.Currency) throw new BusinessException(409, "Webhook không khớp giao dịch.");
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.TransactionCode == code && x.OrderId == intent.OrderId, ct) ?? throw new BusinessException(409, "Giao dịch chưa có trong sổ nội bộ, cần đối soát.");
        if (string.Equals(payload.Status, "canceled", StringComparison.OrdinalIgnoreCase) || string.Equals(payload.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            if (intent.Status == "Paid") throw new BusinessException(409, "Không thể hủy giao dịch đã thu tiền.");
            intent.Status = "Canceled"; payment.PaymentStatus = "Canceled"; intent.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(true, false, intent.OrderId);
        }
        if (!string.Equals(payload.Status, "paid", StringComparison.OrdinalIgnoreCase)) throw new BusinessException(400, "Trạng thái webhook không được hỗ trợ.");
        if (intent.Status == "Paid") { await tx.CommitAsync(ct); return new(true, true, intent.OrderId); }
        if (intent.ReconciliationStatus == "ReconciliationRequired") { await tx.CommitAsync(ct); return new(true, true, intent.OrderId); }
        var order = await db.Orders.SingleAsync(x => x.Id == intent.OrderId, ct);
        var paid = await PaidAsync(order.Id, ct);
        if (intent.Status != "Accepted" || payment.PaymentStatus != "Pending" || payment.Amount != intent.Amount || order.OrderStatus is not ("Confirmed" or "Processing" or "Delivering") || paid + intent.Amount > order.TotalAmount)
        {
            intent.ReconciliationStatus = "ReconciliationRequired"; payment.PaymentStatus = "ReconciliationRequired"; intent.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(true, false, intent.OrderId);
        }
        payment.PaymentStatus = "Paid"; payment.PaidAt = DateTime.UtcNow; intent.Status = "Paid"; intent.UpdatedAtUtc = DateTime.UtcNow;
        order.PaymentStatus = paid + intent.Amount >= order.TotalAmount ? "Paid" : "PartiallyPaid";
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(true, false, intent.OrderId);
    }

    private async Task<decimal> PaidAsync(Guid orderId, CancellationToken ct) => (await db.Payments.Where(x => x.OrderId == orderId && x.PaymentStatus == "Paid").Select(x => x.Amount).ToListAsync(ct)).Sum();
}
