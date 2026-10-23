using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using HoaSap.BLL;
using HoaSap.DAL;
using HoaSap.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace HoaSap.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/management")]
public sealed class CmsOperationsController(HoaSapDbContext db) : ControllerBase
{
    private const int MaxPageSize = 100;
    private const int MaxDateRangeDays = 366;
    private static BusinessException Bad(string message) => new(400, message);
    private static BusinessException NotFound(string message) => new(404, message);
    private static BusinessException Conflict(string message) => new(409, message);

    [HttpGet("vouchers")]
    public async Task<PagedResult<VoucherAdminDto>> Vouchers([FromQuery] VoucherQuery query, CancellationToken ct)
    {
        ValidatePaging(query.Page, query.PageSize);
        var q = db.Vouchers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            q = q.Where(x => x.Code.Contains(search) || x.Description.Contains(search));
        }
        if (query.IsActive is { } active) q = q.Where(x => x.IsActive == active);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.StartDate).ThenBy(x => x.Code)
            .Skip(Offset(query.Page, query.PageSize)).Take(query.PageSize).ToListAsync(ct);
        return new(rows.Select(ToDto).ToList(), total, query.Page, query.PageSize);
    }

    [HttpPut("vouchers/{id:guid}")]
    public async Task<VoucherAdminDto> UpdateVoucher(Guid id, AdminVoucherRequest request, CancellationToken ct)
    {
        ValidateVoucher(request);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var voucher = await db.Vouchers.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound("Voucher not found");
        var code = request.Code.Trim();
        if (await db.Vouchers.AnyAsync(x => x.Id != id && x.Code == code, ct)) throw Conflict("Voucher code exists");
        if (request.UsageLimit < voucher.UsedCount) throw Conflict("UsageLimit cannot be below UsedCount");
        voucher.Code = code;
        voucher.Description = request.Description?.Trim() ?? "";
        voucher.DiscountType = request.DiscountType;
        voucher.DiscountValue = request.DiscountValue;
        voucher.MinOrderAmount = request.MinOrderAmount;
        voucher.MaxDiscountAmount = request.MaxDiscountAmount;
        voucher.StartDate = request.StartDate;
        voucher.EndDate = request.EndDate;
        voucher.UsageLimit = request.UsageLimit;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(voucher);
    }

    [HttpPatch("vouchers/{id:guid}/active")]
    public async Task<IActionResult> SetVoucherActive(Guid id, [FromQuery] bool value, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var voucher = await db.Vouchers.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound("Voucher not found");
        voucher.IsActive = value;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    [HttpGet("orders")]
    public async Task<PagedResult<AdminOrderDto>> Orders([FromQuery] OrdersQuery query, CancellationToken ct)
    {
        ValidatePaging(query.Page, query.PageSize);
        var (from, to) = ValidateDates(query.From, query.To);
        var q = db.Orders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            q = q.Where(x => x.OrderCode.Contains(search) || x.ShippingAddress.Contains(search) ||
                (x.CustomerId != null && db.Customers.Any(c => c.Id == x.CustomerId && (c.Email.Contains(search) || c.FullName.Contains(search) || c.PhoneNumber.Contains(search)))));
        }
        if (!string.IsNullOrWhiteSpace(query.Status)) q = q.Where(x => x.OrderStatus == query.Status.Trim());
        if (!string.IsNullOrWhiteSpace(query.PaymentStatus)) q = q.Where(x => x.PaymentStatus == query.PaymentStatus.Trim());
        if (from is { } f) q = q.Where(x => x.CreatedAt >= f);
        if (to is { } t) q = q.Where(x => x.CreatedAt < t);
        var total = await q.CountAsync(ct);
        var orders = await q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(Offset(query.Page, query.PageSize)).Take(query.PageSize)
            .Include(x => x.Items).ToListAsync(ct);
        var customerIds = orders.Where(x => x.CustomerId.HasValue).Select(x => x.CustomerId!.Value).Distinct().ToArray();
        var customers = await db.Customers.AsNoTracking().Where(x => customerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var productIds = orders.SelectMany(x => x.Items).Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return new(orders.Select(x => ToOrderDto(x, customers, products)).ToList(), total, query.Page, query.PageSize);
    }

    [HttpGet("orders/{id:guid}")]
    public async Task<AdminOrderDto> Order(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw NotFound("Order not found");
        var customer = order.CustomerId is { } customerId
            ? await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId, ct)
            : null;
        var productIds = order.Items.Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var customers = customer is null ? new Dictionary<Guid, Customer>() : new Dictionary<Guid, Customer> { [customer.Id] = customer };
        return ToOrderDto(order, customers, products);
    }

    [HttpGet("dashboard")]
    public async Task<DashboardDto> Dashboard([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var range = ValidateDates(from, to, defaultLast30Days: true);
        var orderQuery = db.Orders.AsNoTracking();
        if (range.From is { } f) orderQuery = orderQuery.Where(x => x.CreatedAt >= f);
        if (range.To is { } t) orderQuery = orderQuery.Where(x => x.CreatedAt < t);
        var orders = await orderQuery.Include(x => x.Items).ToListAsync(ct);
        var validOrders = orders.Where(x => !string.Equals(x.OrderStatus, "Cancelled", StringComparison.OrdinalIgnoreCase)).ToList();
        var orderIds = orders.Select(x => x.Id).ToArray();
        var payments = await db.Payments.AsNoTracking().Where(x => x.OrderId.HasValue && orderIds.Contains(x.OrderId.Value) && x.PaymentStatus == "Paid").ToListAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(x => x.OrderId.HasValue && orderIds.Contains(x.OrderId.Value) && x.RefundStatus == "Processed").ToListAsync(ct);
        var customers = await db.Customers.AsNoTracking().CountAsync(x => !x.IsGuest, ct);
        var productCount = await db.Products.AsNoTracking().CountAsync(ct);
        var productIds = validOrders.SelectMany(x => x.Items).Select(x => x.ProductId).Distinct().ToArray();
        var lowStock = await db.Inventories.AsNoTracking().CountAsync(x => x.QuantityAvailable <= 5, ct);
        var productNames = await db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var status = orders.GroupBy(x => x.OrderStatus).OrderBy(x => x.Key).Select(g => new StatusCount(g.Key, g.Count())).ToList();
        var daily = validOrders.GroupBy(x => x.CreatedAt.Date).OrderBy(g => g.Key).Select(g => new DailyRevenue(g.Key, g.Sum(x => x.TotalAmount))).ToList();
        var top = validOrders.SelectMany(x => x.Items).GroupBy(x => x.ProductId).Select(g => new TopProduct(g.Key, productNames.GetValueOrDefault(g.Key, ""), g.Sum(x => x.Quantity), g.Sum(x => x.FinalPrice * x.Quantity))).OrderByDescending(x => x.Quantity).ThenByDescending(x => x.Revenue).Take(10).ToList();
        var paid = payments.Sum(x => x.Amount);
        var refunded = refunds.Sum(x => x.RefundAmount);
        return new(range.From, range.To, orders.Count, validOrders.Sum(x => x.TotalAmount), paid, refunded, paid - refunded, customers, productCount, lowStock, status, daily, top);
    }

    [HttpGet("reports/orders.csv")]
    public async Task<IActionResult> OrdersCsv([FromQuery] OrdersQuery query, CancellationToken ct)
    {
        ValidatePaging(query.Page, query.PageSize);
        // Reports intentionally use the order-created date cohort; payment/refund status is not a transaction-date cohort.
        var (from, to) = ValidateDates(query.From, query.To);
        // CSV is deliberately bounded independently of UI paging.
        var q = db.Orders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            q = q.Where(x => x.OrderCode.Contains(search) || x.ShippingAddress.Contains(search) ||
                (x.CustomerId != null && db.Customers.Any(c => c.Id == x.CustomerId && (c.Email.Contains(search) || c.FullName.Contains(search) || c.PhoneNumber.Contains(search)))));
        }
        if (!string.IsNullOrWhiteSpace(query.Status)) q = q.Where(x => x.OrderStatus == query.Status.Trim());
        if (!string.IsNullOrWhiteSpace(query.PaymentStatus)) q = q.Where(x => x.PaymentStatus == query.PaymentStatus.Trim());
        if (from is { } f) q = q.Where(x => x.CreatedAt >= f);
        if (to is { } t) q = q.Where(x => x.CreatedAt < t);
        var csvOrders = await q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(10_001).Include(x => x.Items).ToListAsync(ct);
        if (csvOrders.Count > 10_000) throw Bad("CSV export contains more than 10,000 orders; narrow the date or other filters.");
        var customerIds = csvOrders.Where(x => x.CustomerId.HasValue).Select(x => x.CustomerId!.Value).Distinct().ToArray();
        var customers = await db.Customers.AsNoTracking().Where(x => customerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var productIds = csvOrders.SelectMany(x => x.Items).Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var sb = new StringBuilder("orderCode,createdAt,status,paymentStatus,customer,subtotal,discount,shippingFee,totalAmount\r\n");
        foreach (var row in csvOrders.Select(x => ToOrderDto(x, customers, products)))
            sb.AppendJoin(',', Csv(row.OrderCode), Csv(row.CreatedAt.ToString("O", CultureInfo.InvariantCulture)), Csv(row.OrderStatus), Csv(row.PaymentStatus), Csv(row.CustomerName ?? ""), Money(row.SubTotal), Money(row.DiscountAmount), Money(row.ShippingFee), Money(row.TotalAmount)).Append("\r\n");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv; charset=utf-8", "orders.csv");
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Csv(string? value)
    {
        value ??= "";
        var trimmedStart = value.TrimStart(' ', '\t', '\r', '\n');
        if (trimmedStart.Length > 0 && "=+-@".Contains(trimmedStart[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static int Offset(int page, int pageSize)
    {
        ValidatePaging(page, pageSize);
        if ((long)(page - 1) * pageSize > int.MaxValue) throw Bad("page is too large");
        return (page - 1) * pageSize;
    }
    private static void ValidatePaging(int page, int pageSize) { if (page < 1 || pageSize is < 1 or > MaxPageSize) throw Bad("page must be >= 1 and pageSize must be between 1 and 100"); if ((long)(page - 1) * pageSize > int.MaxValue) throw Bad("page is too large"); }
    private static (DateTime? From, DateTime? To) ValidateDates(DateTime? from, DateTime? to, bool defaultLast30Days = false)
    {
        var f = from?.ToUniversalTime(); var t = to?.ToUniversalTime();
        if (defaultLast30Days && f is null && t is null)
        {
            t = DateTime.UtcNow;
            f = t.Value.AddDays(-30);
        }
        else if (f is not null && t is null) t = f.Value.AddDays(MaxDateRangeDays);
        else if (f is null && t is not null) f = t.Value.AddDays(-MaxDateRangeDays);
        if (f is not null && t is not null && f >= t) throw Bad("from must be before to");
        if (f is not null && t is not null && (t.Value - f.Value).TotalDays > MaxDateRangeDays) throw Bad("Date range cannot exceed 366 days");
        return (f, t);
    }
    private static void ValidateVoucher(AdminVoucherRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Code) || r.Code.Trim().Length > 40 || r.StartDate >= r.EndDate || r.UsageLimit < 1) throw Bad("Voucher code/dates/limit invalid");
        if (r.DiscountType is not ("Percentage" or "FixedAmount") || r.DiscountValue <= 0 || r.MinOrderAmount < 0 || r.MaxDiscountAmount is < 0) throw Bad("Voucher discount is invalid");
        if (r.DiscountType == "Percentage" && r.DiscountValue > 100) throw Bad("Percentage must be between 0 and 100");
        if (decimal.Round(r.DiscountValue, 2) != r.DiscountValue || decimal.Round(r.MinOrderAmount, 2) != r.MinOrderAmount || (r.MaxDiscountAmount is { } max && decimal.Round(max, 2) != max)) throw Bad("Voucher amounts support at most two decimal places");
    }
    private static VoucherAdminDto ToDto(Voucher x) => new(x.Id, x.Code, x.Description, x.DiscountType, x.DiscountValue, x.MinOrderAmount, x.MaxDiscountAmount, x.StartDate, x.EndDate, x.UsageLimit, x.UsedCount, x.IsActive);
    private static AdminOrderDto ToOrderDto(Order x, IReadOnlyDictionary<Guid, Customer> customers, IReadOnlyDictionary<Guid, Product> products)
    {
        customers.TryGetValue(x.CustomerId ?? Guid.Empty, out var c);
        return new(x.Id, x.OrderCode, x.CustomerId, c?.FullName, c?.Email, c?.PhoneNumber, x.SubTotal, x.DiscountAmount, x.ShippingFee, x.TotalAmount, x.OrderStatus, x.PaymentStatus, x.ShippingAddress, x.Note, x.CreatedAt, x.Items.Select(i => new AdminOrderItemDto(i.Id, i.ProductId, products.GetValueOrDefault(i.ProductId)?.Name, i.Quantity, i.UnitPrice, i.FinalPrice, i.CustomNote, i.ReferenceImageUrl)).ToList());
    }
}

public sealed class VoucherQuery { [Range(1, int.MaxValue)] public int Page { get; set; } = 1; [Range(1, 100)] public int PageSize { get; set; } = 20; public string? Search { get; set; } public bool? IsActive { get; set; } }
public sealed class OrdersQuery { [Range(1, int.MaxValue)] public int Page { get; set; } = 1; [Range(1, 100)] public int PageSize { get; set; } = 20; public string? Search { get; set; } public string? Status { get; set; } public string? PaymentStatus { get; set; } public DateTime? From { get; set; } public DateTime? To { get; set; } }
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record VoucherAdminDto(Guid Id, string Code, string Description, string DiscountType, decimal DiscountValue, decimal MinOrderAmount, decimal? MaxDiscountAmount, DateTime StartDate, DateTime EndDate, int UsageLimit, int UsedCount, bool IsActive);
public sealed record AdminOrderDto(Guid Id, string OrderCode, Guid? CustomerId, string? CustomerName, string? CustomerEmail, string? CustomerPhone, decimal SubTotal, decimal DiscountAmount, decimal ShippingFee, decimal TotalAmount, string OrderStatus, string PaymentStatus, string ShippingAddress, string Note, DateTime CreatedAt, IReadOnlyList<AdminOrderItemDto> Items);
public sealed record AdminOrderItemDto(Guid Id, Guid ProductId, string? ProductName, int Quantity, decimal UnitPrice, decimal FinalPrice, string CustomNote, string ReferenceImageUrl);
public sealed record StatusCount(string Status, int Count);
public sealed record DailyRevenue(DateTime Date, decimal Amount);
public sealed record TopProduct(Guid ProductId, string Name, int Quantity, decimal Revenue);
public sealed record DashboardDto(DateTime? From, DateTime? To, int OrderCount, decimal Revenue, decimal PaidAmount, decimal RefundAmount, decimal NetCollected, int CustomerCount, int ProductCount, int LowStockCount, IReadOnlyList<StatusCount> OrdersByStatus, IReadOnlyList<DailyRevenue> DailyRevenue, IReadOnlyList<TopProduct> TopProducts);
