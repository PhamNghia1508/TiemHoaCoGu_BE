using System.ComponentModel.DataAnnotations;
using System.Data;
using HoaSap.DAL;
using HoaSap.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HoaSap.BLL.CustomOrders;

public sealed record CustomRequestDto(Guid Id, Guid ProductId, int Quantity, string Description, string ReferenceImageUrl, DateTime? RequestedCompletionDate, string Status, DateTime CreatedAt);
public sealed record CustomQuoteRevisionDto(int Version, decimal? UnitPrice, decimal? TotalAmount, decimal? DepositAmount, decimal PaidAmount, decimal BalanceAmount, DateTime CompletionDate, DateTime ExpiresAt, string AdminNote, DateTime CreatedAt, string Status);
public sealed record CustomQuoteDto(Guid Id, Guid RequestId, int CurrentRevision, string Status, DateTime? ExpiresAt, IReadOnlyList<CustomQuoteRevisionDto> Revisions, Guid? OrderId, decimal PaidAmount, decimal BalanceAmount);
public sealed class CustomRequestInput
{
    public Guid ProductId { get; set; }
    [Range(1, 1000)] public int Quantity { get; set; } = 1;
    [Required, MaxLength(4000)] public string Description { get; set; } = "";
    [Url, MaxLength(2000)] public string? ReferenceImageUrl { get; set; }
    public DateTime? RequestedCompletionDate { get; set; }
}
public sealed class CustomQuoteInput
{
    [Range(0, 1000000000000d)] public decimal UnitPrice { get; set; }
    [Range(0, 100)] public decimal DepositPercent { get; set; } = 50;
    public DateTime CompletionDate { get; set; }
    [Required, MaxLength(2000)] public string AdminNote { get; set; } = "";
    public DateTime? ExpiresAt { get; set; }
}
public sealed class CustomApproveInput { [Range(1, int.MaxValue)] public int Version { get; set; } }
public sealed class CustomCheckoutInput
{
    [Required, MaxLength(2000)] public string ShippingAddress { get; set; } = "";
    [MaxLength(1000)] public string? Note { get; set; }
    [Required, MaxLength(100)] public string IdempotencyKey { get; set; } = "";
}
public sealed record CustomCheckoutDto(Guid OrderId, string OrderCode, int QuoteVersion, decimal TotalAmount, decimal DepositAmount, decimal PaidAmount, decimal BalanceAmount, string PaymentStatus);
public sealed record CustomLifecycleDto(Guid RequestId, Guid OrderId, string Status, DateTime? ScheduledCompletionDate, DateTime? ProductionStartedAt, DateTime? CompletedAt, decimal RequiredDeposit, decimal PaidAmount, decimal BalanceAmount, string DeliveryStatus);

/// <summary>Made-to-order quotations do not reserve stock from the ready-made catalog.</summary>
public sealed class CustomOrderService(HoaSapDbContext db)
{
    private static BusinessException Error(int code, string message) => new(code, message);
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();

    public async Task<CustomRequestDto> CreateAsync(Guid customerId, CustomRequestInput input, CancellationToken ct = default)
    {
        await ValidateRequestAsync(input, ct);
        var request = new CustomOrderRequest { CustomerId = customerId };
        Apply(request, input);
        db.Add(request);
        db.Add(new CustomQuotation { CustomOrderRequestId = request.Id });
        await db.SaveChangesAsync(ct);
        return Map(request);
    }

    public async Task<IReadOnlyList<CustomRequestDto>> MineAsync(Guid customerId, CancellationToken ct = default) =>
        (await db.Set<CustomOrderRequest>().AsNoTracking().Where(x => x.CustomerId == customerId).OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct)).Select(Map).ToList();

    public async Task<IReadOnlyList<CustomRequestDto>> StaffRequestsAsync(CancellationToken ct = default) =>
        (await db.Set<CustomOrderRequest>().AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct)).Select(Map).ToList();

    public async Task<CustomRequestDto> ReviseRequestAsync(Guid id, Guid customerId, CustomRequestInput input, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var request = await OwnedAsync(id, customerId, ct);
        if (request.Status != "Requested") throw Error(409, "Yêu cầu đã báo giá; vui lòng yêu cầu một phiên bản báo giá mới.");
        await ValidateRequestAsync(input, ct);
        Apply(request, input);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(request);
    }

    public async Task<CustomQuoteDto> QuoteAsync(Guid id, Guid? customerId, bool staff, bool admin, CancellationToken ct = default)
    {
        var request = await db.Set<CustomOrderRequest>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error(404, "Không tìm thấy yêu cầu.");
        if (!staff && request.CustomerId != customerId) throw Error(403, "Bạn không có quyền xem yêu cầu này.");
        var quote = await db.Set<CustomQuotation>().AsNoTracking().SingleAsync(x => x.CustomOrderRequestId == id, ct);
        var revisions = await db.Set<CustomQuotationRevision>().AsNoTracking().Where(x => x.CustomQuotationId == quote.Id).OrderBy(x => x.Version).ToListAsync(ct);
        var checkout = await db.Set<CustomOrderCheckout>().AsNoTracking().SingleOrDefaultAsync(x => x.CustomQuotationId == quote.Id, ct);
        var paid = checkout is null ? 0 : await PaidAsync(checkout.OrderId, ct);
        var total = revisions.LastOrDefault()?.TotalAmount ?? 0;
        var hideMoney = staff && !admin;
        return new(quote.Id, id, quote.CurrentRevision, quote.Status, quote.ExpiresAt,
            revisions.Select(x => new CustomQuoteRevisionDto(x.Version, hideMoney ? null : x.UnitPrice, hideMoney ? null : x.TotalAmount, hideMoney ? null : x.DepositAmount,
                hideMoney ? 0 : paid, hideMoney ? 0 : Math.Max(0, x.TotalAmount - paid), x.CompletionDate, x.ExpiresAt, hideMoney ? "" : x.AdminNote, x.CreatedAt, x.Status)).ToList(),
            checkout?.OrderId, hideMoney ? 0 : paid, hideMoney ? 0 : Math.Max(0, total - paid));
    }

    public async Task<CustomQuoteDto> ReviseAsync(Guid requestId, Guid adminId, CustomQuoteInput input, CancellationToken ct = default)
    {
        if (input is null || input.UnitPrice < 0 || input.UnitPrice > 1_000_000_000_000m || decimal.Round(input.UnitPrice, 2) != input.UnitPrice || input.DepositPercent is < 0 or > 100 || string.IsNullOrWhiteSpace(input.AdminNote))
            throw Error(400, "Báo giá chưa hợp lệ.");
        var now = DateTime.UtcNow;
        var completion = Utc(input.CompletionDate);
        var expiry = Utc(input.ExpiresAt ?? (now.AddDays(7) < completion ? now.AddDays(7) : completion));
        if (completion <= now || expiry <= now || expiry > completion) throw Error(400, "Thời hạn báo giá phải trước ngày hoàn thành và ở tương lai.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var request = await db.Set<CustomOrderRequest>().SingleOrDefaultAsync(x => x.Id == requestId, ct) ?? throw Error(404, "Không tìm thấy yêu cầu.");
        var quote = await db.Set<CustomQuotation>().SingleAsync(x => x.CustomOrderRequestId == requestId, ct);
        if (await db.Set<CustomOrderCheckout>().AnyAsync(x => x.CustomQuotationId == quote.Id, ct) || quote.Status is "Cancelled" or "Completed")
            throw Error(409, "Báo giá đã chốt, không thể thay đổi giá của đơn đã tạo.");
        var total = decimal.Round(input.UnitPrice * request.Quantity, 2);
        var revision = new CustomQuotationRevision
        {
            CustomQuotationId = quote.Id, Version = quote.CurrentRevision + 1, UnitPrice = input.UnitPrice, Quantity = request.Quantity,
            TotalAmount = total, DepositAmount = decimal.Round(total * input.DepositPercent / 100m, 2, MidpointRounding.AwayFromZero),
            CompletionDate = completion, ExpiresAt = expiry, CreatedBy = adminId, AdminNote = input.AdminNote.Trim()
        };
        db.Add(revision);
        quote.CurrentRevision = revision.Version;
        quote.ExpiresAt = expiry;
        quote.Status = request.Status = "Quoted";
        quote.UpdatedAt = request.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await QuoteAsync(requestId, request.CustomerId, false, true, ct);
    }

    public async Task<CustomQuoteDto> ApproveAsync(Guid id, Guid customerId, CustomApproveInput input, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var request = await OwnedAsync(id, customerId, ct);
        var quote = await db.Set<CustomQuotation>().SingleAsync(x => x.CustomOrderRequestId == id, ct);
        var revision = await db.Set<CustomQuotationRevision>().SingleOrDefaultAsync(x => x.CustomQuotationId == quote.Id && x.Version == input.Version, ct) ?? throw Error(409, "Phiên bản báo giá không tồn tại.");
        if (quote.Status == "Approved" && quote.CurrentRevision == input.Version && revision.Status == "Approved")
        {
            await tx.CommitAsync(ct);
            return await QuoteAsync(id, customerId, false, true, ct);
        }
        if (quote.Status != "Quoted" || quote.CurrentRevision != input.Version || revision.Status != "Sent" || revision.ExpiresAt <= DateTime.UtcNow)
            throw Error(409, "Báo giá đã cũ hoặc hết hạn.");
        quote.Status = request.Status = revision.Status = "Approved";
        quote.UpdatedAt = request.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await QuoteAsync(id, customerId, false, true, ct);
    }

    public async Task<CustomCheckoutDto> CheckoutAsync(Guid id, Guid customerId, CustomCheckoutInput input, CancellationToken ct = default)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.IdempotencyKey) || input.IdempotencyKey.Length > 100 || string.IsNullOrWhiteSpace(input.ShippingAddress)) throw Error(400, "Thiếu địa chỉ hoặc mã idempotency.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var request = await OwnedAsync(id, customerId, ct);
        var quote = await db.Set<CustomQuotation>().SingleAsync(x => x.CustomOrderRequestId == id, ct);
        var key = input.IdempotencyKey.Trim();
        var old = await db.Set<CustomOrderCheckout>().SingleOrDefaultAsync(x => x.CustomerId == customerId && x.IdempotencyKey == key, ct);
        if (old is not null)
        {
            var order = await db.Orders.SingleAsync(x => x.Id == old.OrderId, ct);
            if (old.CustomQuotationId != quote.Id || order.ShippingAddress != input.ShippingAddress.Trim() || order.Note != (input.Note?.Trim() ?? ""))
                throw Error(409, "Mã idempotency đã dùng cho nội dung khác.");
            var oldRevision = await db.Set<CustomQuotationRevision>().SingleAsync(x => x.CustomQuotationId == quote.Id && x.Version == old.QuotationVersion, ct);
            await tx.CommitAsync(ct);
            return await CheckoutDto(order, oldRevision, ct);
        }
        if (await db.Set<CustomOrderCheckout>().AnyAsync(x => x.CustomQuotationId == quote.Id, ct)) throw Error(409, "Báo giá này đã có đơn hàng.");
        var revision = await db.Set<CustomQuotationRevision>().SingleOrDefaultAsync(x => x.CustomQuotationId == quote.Id && x.Version == quote.CurrentRevision, ct) ?? throw Error(409, "Chưa có báo giá.");
        if (quote.Status != "Approved" || revision.Status != "Approved" || revision.ExpiresAt <= DateTime.UtcNow) throw Error(409, "Báo giá chưa được duyệt hoặc đã hết hạn.");
        var created = new Order
        {
            CustomerId = customerId, OrderCode = $"CUS-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}", SubTotal = revision.TotalAmount, TotalAmount = revision.TotalAmount,
            OrderStatus = "Confirmed", PaymentStatus = revision.TotalAmount == 0 ? "Paid" : "Unpaid", ShippingAddress = input.ShippingAddress.Trim(), Note = input.Note?.Trim() ?? ""
        };
        // The accepted quote is an all-inclusive price. Delivery is created by operations after production starts.
        created.Items.Add(new OrderItem { OrderId = created.Id, ProductId = request.ProductId, Quantity = request.Quantity, UnitPrice = revision.UnitPrice, FinalPrice = revision.UnitPrice, CustomNote = request.Description, ReferenceImageUrl = request.ReferenceImageUrl });
        db.Add(created);
        db.Add(new CustomOrderCheckout { CustomQuotationId = quote.Id, QuotationVersion = revision.Version, CustomerId = customerId, OrderId = created.Id, IdempotencyKey = key });
        quote.Status = request.Status = "CheckedOut";
        quote.UpdatedAt = request.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await CheckoutDto(created, revision, ct);
    }

    public async Task<CustomLifecycleDto> StartProductionAsync(Guid id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var context = await LifecycleContext(id, ct);
        if (context.Order.OrderStatus is "Cancelled" or "Expired" or "Completed") throw Error(409, "Đơn đã kết thúc.");
        if (context.Paid < context.Revision.DepositAmount) throw Error(409, "Chưa đủ tiền cọc để bắt đầu sản xuất.");
        if (context.Quote.Status is not ("CheckedOut" or "Production")) throw Error(409, "Đơn chưa sẵn sàng sản xuất.");
        if (context.Quote.Status == "Production") { await tx.CommitAsync(ct); return ToLifecycle(context, "Production", context.Order.OrderStatus); }
        context.Quote.Status = context.Request.Status = "Production";
        context.Request.ProductionStartedAt ??= DateTime.UtcNow;
        context.Quote.UpdatedAt = context.Request.UpdatedAt = DateTime.UtcNow;
        context.Order.OrderStatus = "Processing";
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToLifecycle(context, "Production", "Preparing");
    }

    public async Task<CustomLifecycleDto> CompleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var context = await LifecycleContext(id, ct);
        if (context.Request.ProductionStartedAt is null || context.Order.OrderStatus is "Cancelled" or "Expired") throw Error(409, "Đơn chưa sản xuất hoặc đã hủy.");
        if (context.Paid < context.Revision.TotalAmount) throw Error(409, "Chưa thanh toán đủ tổng tiền.");
        var items = await db.OrderItems.Where(x => x.OrderId == context.Order.Id).ToListAsync(ct);
        var delivered = await db.DeliveryItems.Where(x => db.Deliveries.Any(d => d.Id == x.DeliveryId && d.OrderId == context.Order.Id && d.DeliveryStatus == "Delivered")).ToListAsync(ct);
        if (items.Any(x => delivered.Where(d => d.OrderItemId == x.Id).Sum(d => d.ShippedQuantity) < x.Quantity)) throw Error(409, "Chưa giao đủ sản phẩm.");
        context.Quote.Status = context.Request.Status = context.Order.OrderStatus = "Completed";
        context.Request.CompletedAt ??= DateTime.UtcNow;
        context.Quote.UpdatedAt = context.Request.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToLifecycle(context, "Completed", "Delivered");
    }

    private async Task ValidateRequestAsync(CustomRequestInput input, CancellationToken ct)
    {
        if (input is null || input.Quantity is < 1 or > 1000 || string.IsNullOrWhiteSpace(input.Description) || input.Description.Length > 4000) throw Error(400, "Thông tin yêu cầu chưa hợp lệ.");
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.ProductId && x.IsActive, ct) ?? throw Error(404, "Không tìm thấy sản phẩm.");
        if (!product.IsCustomizable && !product.IsPreOrder) throw Error(400, "Sản phẩm không hỗ trợ tùy biến hoặc đặt trước.");
        if (input.RequestedCompletionDate is { } date && Utc(date) <= DateTime.UtcNow) throw Error(400, "Ngày mong muốn phải ở tương lai.");
    }
    private static void Apply(CustomOrderRequest request, CustomRequestInput input)
    {
        request.ProductId = input.ProductId; request.Quantity = input.Quantity; request.Description = input.Description.Trim(); request.ReferenceImageUrl = input.ReferenceImageUrl?.Trim() ?? "";
        request.RequestedCompletionDate = input.RequestedCompletionDate is { } date ? Utc(date) : null; request.UpdatedAt = DateTime.UtcNow;
    }
    private async Task<CustomOrderRequest> OwnedAsync(Guid id, Guid customerId, CancellationToken ct)
    {
        var request = await db.Set<CustomOrderRequest>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error(404, "Không tìm thấy yêu cầu.");
        if (request.CustomerId != customerId) throw Error(403, "Bạn không có quyền với yêu cầu này.");
        return request;
    }
    private async Task<decimal> PaidAsync(Guid orderId, CancellationToken ct) => (await db.Payments.Where(x => x.OrderId == orderId && x.PaymentStatus == "Paid").Select(x => x.Amount).ToListAsync(ct)).Sum();
    private async Task<CustomCheckoutDto> CheckoutDto(Order order, CustomQuotationRevision revision, CancellationToken ct)
    {
        var paid = await PaidAsync(order.Id, ct);
        return new(order.Id, order.OrderCode, revision.Version, revision.TotalAmount, revision.DepositAmount, paid, Math.Max(0, revision.TotalAmount - paid), order.PaymentStatus);
    }
    private sealed record LifecycleState(CustomOrderRequest Request, CustomQuotation Quote, CustomQuotationRevision Revision, Order Order, decimal Paid);
    private async Task<LifecycleState> LifecycleContext(Guid id, CancellationToken ct)
    {
        var request = await db.Set<CustomOrderRequest>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error(404, "Không tìm thấy yêu cầu.");
        var quote = await db.Set<CustomQuotation>().SingleAsync(x => x.CustomOrderRequestId == id, ct);
        var checkout = await db.Set<CustomOrderCheckout>().SingleOrDefaultAsync(x => x.CustomQuotationId == quote.Id, ct) ?? throw Error(409, "Chưa checkout.");
        var revision = await db.Set<CustomQuotationRevision>().SingleAsync(x => x.CustomQuotationId == quote.Id && x.Version == checkout.QuotationVersion, ct);
        var order = await db.Orders.SingleAsync(x => x.Id == checkout.OrderId, ct);
        return new(request, quote, revision, order, await PaidAsync(order.Id, ct));
    }
    private static CustomLifecycleDto ToLifecycle(LifecycleState state, string status, string delivery) => new(state.Request.Id, state.Order.Id, status, state.Revision.CompletionDate, state.Request.ProductionStartedAt, state.Request.CompletedAt, state.Revision.DepositAmount, state.Paid, Math.Max(0, state.Revision.TotalAmount - state.Paid), delivery);
    private static CustomRequestDto Map(CustomOrderRequest request) => new(request.Id, request.ProductId, request.Quantity, request.Description, request.ReferenceImageUrl, request.RequestedCompletionDate, request.Status, request.CreatedAt);
}
