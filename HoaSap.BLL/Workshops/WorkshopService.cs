using System.Data;
using System.Text.Json;
using HoaSap.BLL;
using HoaSap.DAL;
using HoaSap.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HoaSap.BLL.Workshops;

public sealed class WorkshopService(HoaSapDbContext db)
{
    private const decimal DepositRate = 0.30m;
    private static readonly string[] ManualMethods = ["Cash", "BankTransfer"];
    private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public async Task<IReadOnlyList<WorkshopListDto>> ListAsync(bool staff = false, CancellationToken ct = default) =>
        await db.Workshops.AsNoTracking().Where(x => staff || x.IsActive).OrderBy(x => x.Name)
            .Select(x => new WorkshopListDto(x.Id, x.Code, x.Name, x.ShortName, x.Category, x.BasePrice, x.Duration, x.MaxCapacity, x.Description, x.IsFeatured, x.IsActive)).ToListAsync(ct);

    public async Task<WorkshopDto> GetAsync(Guid id, bool staff = false, CancellationToken ct = default)
    {
        var x = await db.Workshops.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && (staff || x.IsActive), ct)
            ?? throw new BusinessException(404, "Không tìm thấy workshop.");
        return ToDto(x);
    }

    public async Task<WorkshopDto> CreateAsync(WorkshopUpsertDto r, CancellationToken ct = default)
    {
        await ValidateWorkshopAsync(r, ct);
        var x = new Workshop { Code = r.Code.Trim(), Name = r.Name.Trim(), ShortName = r.ShortName?.Trim() ?? "", Category = r.Category?.Trim() ?? "", BasePrice = r.BasePrice, Duration = r.Duration, MaxCapacity = r.MaxCapacity, Description = r.Description ?? "", LeadContent = r.LeadContent ?? "", GoalsJson = NormalizeArrayJson(r.GoalsJson), IncludesJson = NormalizeArrayJson(r.IncludesJson), ForWho = r.ForWho ?? "", IsFeatured = r.IsFeatured, IsActive = r.IsActive, InstructorId = r.InstructorId };
        db.Workshops.Add(x); await db.SaveChangesAsync(ct); return ToDto(x);
    }

    public async Task<WorkshopDto> UpdateAsync(Guid id, WorkshopUpsertDto r, CancellationToken ct = default)
    {
        await ValidateWorkshopAsync(r, ct);
        var x = await db.Workshops.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException(404, "Không tìm thấy workshop.");
        x.Code = r.Code.Trim(); x.Name = r.Name.Trim(); x.ShortName = r.ShortName?.Trim() ?? ""; x.Category = r.Category?.Trim() ?? ""; x.BasePrice = r.BasePrice; x.Duration = r.Duration; x.MaxCapacity = r.MaxCapacity; x.Description = r.Description ?? ""; x.LeadContent = r.LeadContent ?? ""; x.GoalsJson = NormalizeArrayJson(r.GoalsJson); x.IncludesJson = NormalizeArrayJson(r.IncludesJson); x.ForWho = r.ForWho ?? ""; x.IsFeatured = r.IsFeatured; x.IsActive = r.IsActive; x.InstructorId = r.InstructorId;
        await db.SaveChangesAsync(ct); return ToDto(x);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var x = await db.Workshops.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException(404, "Không tìm thấy workshop.");
        x.IsActive = false; await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SessionDto>> SessionsAsync(Guid workshopId, bool staff = false, CancellationToken ct = default) =>
        await db.WorkshopSessions.AsNoTracking().Where(x => x.WorkshopId == workshopId && (staff || x.SessionStatus != "Cancelled")).OrderBy(x => x.SessionDate).ThenBy(x => x.StartTime)
            .Select(x => new SessionDto(x.Id, x.WorkshopId, x.SessionDate, x.StartTime, x.EndTime, x.Location, x.Capacity, x.BookedSlots, x.SessionStatus)).ToListAsync(ct);

    public async Task<SessionDto> CreateSessionAsync(Guid workshopId, SessionUpsertDto r, CancellationToken ct = default)
    {
        ValidateSession(r);
        var w = await db.Workshops.FirstOrDefaultAsync(x => x.Id == workshopId && x.IsActive, ct) ?? throw new BusinessException(404, "Workshop không tồn tại hoặc đã ngừng hoạt động.");
        if (r.Capacity > w.MaxCapacity) throw new BusinessException(400, "Sức chứa ca học không được vượt quá workshop.");
        var x = new WorkshopSession { WorkshopId = workshopId, SessionDate = r.SessionDate.Date, StartTime = r.StartTime, EndTime = r.EndTime, Location = r.Location.Trim(), Capacity = r.Capacity, SessionStatus = "Open" };
        db.WorkshopSessions.Add(x); await db.SaveChangesAsync(ct); return ToDto(x);
    }

    public async Task<SessionDto> UpdateSessionAsync(Guid id, SessionUpsertDto r, CancellationToken ct = default)
    {
        ValidateSession(r);
        var x = await db.WorkshopSessions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException(404, "Không tìm thấy ca học.");
        var w = await db.Workshops.AsNoTracking().FirstAsync(w => w.Id == x.WorkshopId, ct);
        if (r.Capacity > w.MaxCapacity || r.Capacity < x.BookedSlots) throw new BusinessException(400, "Sức chứa ca học không hợp lệ.");
        x.SessionDate = r.SessionDate.Date; x.StartTime = r.StartTime; x.EndTime = r.EndTime; x.Location = r.Location.Trim(); x.Capacity = r.Capacity; SetFullOrOpen(x);
        await db.SaveChangesAsync(ct); return ToDto(x);
    }

    public async Task CancelSessionAsync(Guid id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var x = await db.WorkshopSessions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException(404, "Không tìm thấy ca học.");
        if (x.SessionStatus == "Cancelled") { await tx.CommitAsync(ct); return; }
        var active = await db.WorkshopBookings.AnyAsync(b => b.SessionId == id && b.BookingStatus != "cancelled" && b.BookingStatus != "expired" && b.BookingStatus != "attended" && b.BookingStatus != "no_show", ct);
        if (active) throw new BusinessException(409, "Không thể hủy ca học đang có booking hoạt động.");
        x.SessionStatus = "Cancelled"; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task<BookingDto> HoldBookingAsync(Guid sessionId, BookingRequest r, Guid? customerId, CancellationToken ct = default)
    {
        if (r.TicketCount < 1) throw new BusinessException(400, "Số vé không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await ExpirePendingBookingsCoreAsync(ct);
        var s = await db.WorkshopSessions.FirstOrDefaultAsync(x => x.Id == sessionId, ct) ?? throw new BusinessException(404, "Không tìm thấy ca học.");
        var w = await db.Workshops.FirstOrDefaultAsync(x => x.Id == s.WorkshopId, ct) ?? throw new BusinessException(404, "Không tìm thấy workshop.");
        if (!w.IsActive || s.SessionStatus is "Cancelled" or "Closed" || SessionStartUtc(s) <= DateTime.UtcNow) throw new BusinessException(409, "Ca học không còn nhận booking.");
        if (s.BookedSlots + r.TicketCount > s.Capacity) throw new BusinessException(409, "Ca học không đủ chỗ.");
        s.BookedSlots += r.TicketCount; SetFullOrOpen(s);
        var now = DateTime.UtcNow; var total = decimal.Round(w.BasePrice * r.TicketCount, 0); var deposit = decimal.Round(total * DepositRate, 0);
        var b = new WorkshopBooking { SessionId = sessionId, CustomerId = customerId, BookingCode = "WS-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), CustomerName = r.CustomerName?.Trim() ?? "", CustomerPhone = r.CustomerPhone?.Trim() ?? "", CustomerEmail = r.CustomerEmail?.Trim() ?? "", TicketCount = r.TicketCount, TotalAmount = total, DepositAmount = deposit, RemainingAmount = total, BookingStatus = total == 0 ? "confirmed" : "pending", HoldExpiresAt = now.AddMinutes(10), CreatedAt = now };
        db.WorkshopBookings.Add(b); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return ToDto(b);
    }

    public async Task ExpirePendingBookingsAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); await ExpirePendingBookingsCoreAsync(ct); await tx.CommitAsync(ct);
    }

    private async Task ExpirePendingBookingsCoreAsync(CancellationToken ct)
    {
        var list = await db.WorkshopBookings.Where(x => x.BookingStatus == "pending" && x.HoldExpiresAt <= DateTime.UtcNow).ToListAsync(ct);
        foreach (var b in list) { b.BookingStatus = "expired"; var s = await db.WorkshopSessions.FirstOrDefaultAsync(s => s.Id == b.SessionId, ct); if (s != null) { s.BookedSlots = Math.Max(0, s.BookedSlots - b.TicketCount); SetFullOrOpen(s); } }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BookingDto>> MyBookingsAsync(Guid customerId, CancellationToken ct = default) => (await db.WorkshopBookings.AsNoTracking().Where(x => x.CustomerId == customerId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct)).Select(ToDto).ToList();
    public async Task<BookingDto> BookingAsync(Guid id, Guid? customerId, bool staff, CancellationToken ct = default) => ToDto(await db.WorkshopBookings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && (staff || x.CustomerId == customerId), ct) ?? throw new BusinessException(404, "Không tìm thấy booking."));

    public async Task CancelBookingAsync(Guid id, Guid? customerId, bool staff, string? reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var b = await db.WorkshopBookings.FirstOrDefaultAsync(x => x.Id == id && (staff || x.CustomerId == customerId), ct) ?? throw new BusinessException(404, "Không tìm thấy booking.");
        if (b.BookingStatus == "cancelled") { await tx.CommitAsync(ct); return; }
        if (b.BookingStatus is "expired" or "attended" or "no_show") throw new BusinessException(409, "Booking không thể hủy ở trạng thái hiện tại.");
        b.BookingStatus = "cancelled"; b.CancelReason = reason?.Trim() ?? "";
        var s = await db.WorkshopSessions.FirstOrDefaultAsync(x => x.Id == b.SessionId, ct); if (s != null) { s.BookedSlots = Math.Max(0, s.BookedSlots - b.TicketCount); SetFullOrOpen(s); }
        var paid = await PaidAmountAsync(b.Id, ct);
        if (paid > 0 && !await db.Refunds.AnyAsync(x => x.BookingId == b.Id && x.RefundStatus == "Requested", ct)) db.Refunds.Add(new Refund { BookingId = b.Id, RefundAmount = paid, RefundReason = "Yêu cầu hoàn do hủy booking", RefundStatus = "Requested", ApprovedBy = "", ProcessedAt = null });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task RecordPaymentAsync(Guid? bookingId, Guid? orderId, ManualPaymentRequest r, Guid staffId, CancellationToken ct = default)
    {
        if ((bookingId is null) == (orderId is null) || !ManualMethods.Contains(r.PaymentMethod, StringComparer.OrdinalIgnoreCase) || r.Amount <= 0) throw new BusinessException(400, "Thanh toán thủ công không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await db.Payments.FirstOrDefaultAsync(x => x.TransactionCode == r.TransactionCode.Trim(), ct);
        if (existing != null) { if ((bookingId != null && existing.BookingId == bookingId || orderId != null && existing.OrderId == orderId) && existing.PaymentMethod.Equals(r.PaymentMethod, StringComparison.OrdinalIgnoreCase) && existing.Amount == r.Amount) { await tx.CommitAsync(ct); return; } throw new BusinessException(409, "Mã giao dịch đã được dùng cho dữ liệu khác."); }
        if (bookingId is not null)
        {
            var b = await db.WorkshopBookings.FirstOrDefaultAsync(x => x.Id == bookingId, ct) ?? throw new BusinessException(404, "Không tìm thấy booking.");
            var s = await db.WorkshopSessions.FirstOrDefaultAsync(x => x.Id == b.SessionId, ct) ?? throw new BusinessException(404, "Không tìm thấy ca học.");
            var w = await db.Workshops.FirstOrDefaultAsync(x => x.Id == s.WorkshopId, ct) ?? throw new BusinessException(404, "Không tìm thấy workshop.");
            if (!w.IsActive || s.SessionStatus is "Cancelled" or "Closed" || b.BookingStatus is "expired" or "cancelled" or "attended" or "no_show" || (b.BookingStatus == "pending" && b.HoldExpiresAt <= DateTime.UtcNow) || SessionStartUtc(s) <= DateTime.UtcNow) throw new BusinessException(409, "Booking không còn hiệu lực để thanh toán.");
            var paid = await PaidAmountAsync(b.Id, ct); var after = paid + r.Amount;
            if (paid == 0 && r.Amount < b.DepositAmount) throw new BusinessException(400, "Lần thanh toán đầu phải đủ tiền cọc tối thiểu.");
            if (paid > 0 && DateTime.UtcNow >= SessionStartUtc(s).AddHours(-24)) throw new BusinessException(409, "Đã quá hạn thanh toán phần còn lại trước buổi học 24 giờ; cần nhân viên xử lý ngoại lệ ngoài hệ thống.");
            if (paid == 0 && DateTime.UtcNow >= SessionStartUtc(s).AddHours(-24) && r.Amount < b.TotalAmount) throw new BusinessException(409, "Booking trong 24 giờ trước buổi học phải thanh toán đủ ngay lần đầu.");
            if (after > b.TotalAmount) throw new BusinessException(400, "Số tiền thanh toán vượt quá tổng booking.");
            db.Payments.Add(new Payment { BookingId = b.Id, TransactionCode = r.TransactionCode.Trim(), PaymentType = paid == 0 ? "Deposit" : "Remaining", PaymentMethod = r.PaymentMethod.Trim(), Amount = r.Amount, PaymentStatus = "Paid", PaidAt = DateTime.UtcNow, PaymentGatewayResponse = "Manual staff record" });
            b.RemainingAmount = Math.Max(0, b.TotalAmount - after); if (after >= b.DepositAmount) b.BookingStatus = "confirmed";
        }
        else
        {
            var o = await db.Orders.FirstOrDefaultAsync(x => x.Id == orderId, ct) ?? throw new BusinessException(404, "Không tìm thấy order.");
            if (o.OrderStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)) throw new BusinessException(409, "Order đã hủy không thể thanh toán.");
            var amounts = await db.Payments.Where(x => x.OrderId == o.Id && x.PaymentStatus == "Paid").Select(x => x.Amount).ToListAsync(ct); var paid = amounts.Sum(); if (paid + r.Amount > o.TotalAmount) throw new BusinessException(400, "Số tiền thanh toán vượt quá tổng order.");
            db.Payments.Add(new Payment { OrderId = o.Id, TransactionCode = r.TransactionCode.Trim(), PaymentType = "Full", PaymentMethod = r.PaymentMethod.Trim(), Amount = r.Amount, PaymentStatus = "Paid", PaidAt = DateTime.UtcNow, PaymentGatewayResponse = "Manual staff record" }); o.PaymentStatus = paid + r.Amount >= o.TotalAmount ? "Paid" : "PartiallyPaid";
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task CheckInAsync(Guid id, bool noShow, CancellationToken ct = default)
    {
        var b = await db.WorkshopBookings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException(404, "Không tìm thấy booking.");
        if (b.BookingStatus != "confirmed") throw new BusinessException(409, "Chỉ booking đã xác nhận mới được check-in/no-show.");
        if (!noShow && await PaidAmountAsync(id, ct) < b.TotalAmount) throw new BusinessException(409, "Chỉ booking đã thanh toán đủ mới được check-in.");
        if (noShow) b.BookingStatus = "no_show"; else { b.BookingStatus = "attended"; b.CheckedInAt = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
    }

    public async Task RefundAsync(Guid id, RefundRequest r, Guid staffId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await db.WorkshopBookings.AnyAsync(x => x.Id == id, ct)) throw new BusinessException(404, "Không tìm thấy booking.");
        var paid = await PaidAmountAsync(id, ct); var refunded = (await db.Refunds.Where(x => x.BookingId == id && x.RefundStatus != "Rejected").Select(x => x.RefundAmount).ToListAsync(ct)).Sum();
        if (r.Amount <= 0 || r.Amount > paid - refunded) throw new BusinessException(400, "Số tiền hoàn vượt quá số tiền đã thanh toán còn lại.");
        db.Refunds.Add(new Refund { BookingId = id, RefundAmount = r.Amount, RefundReason = r.Reason.Trim(), RefundStatus = "Requested", ApprovedBy = staffId.ToString() }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    private async Task<decimal> PaidAmountAsync(Guid bookingId, CancellationToken ct) => (await db.Payments.Where(x => x.BookingId == bookingId && x.PaymentStatus == "Paid").Select(x => x.Amount).ToListAsync(ct)).Sum();
    private static DateTime SessionStartUtc(WorkshopSession s) => TimeZoneInfo.ConvertTimeToUtc(s.SessionDate.Date.Add(s.StartTime), VietnamTimeZone);
    private static void SetFullOrOpen(WorkshopSession s) { if (s.SessionStatus is "Cancelled" or "Closed") return; s.SessionStatus = s.BookedSlots >= s.Capacity ? "Full" : "Open"; }
    private async Task ValidateWorkshopAsync(WorkshopUpsertDto r, CancellationToken ct)
    {
        ValidateWorkshop(r); ValidateArrayJson(r.GoalsJson); ValidateArrayJson(r.IncludesJson);
        if (r.InstructorId is not null && !await db.Instructors.AnyAsync(x => x.Id == r.InstructorId, ct)) throw new BusinessException(400, "Instructor không tồn tại.");
    }
    private static void ValidateWorkshop(WorkshopUpsertDto r) { if (string.IsNullOrWhiteSpace(r.Code) || string.IsNullOrWhiteSpace(r.Name) || r.BasePrice < 0 || r.MaxCapacity < 1) throw new BusinessException(400, "Thông tin workshop không hợp lệ."); }
    private static void ValidateSession(SessionUpsertDto r) { if (r.Capacity < 1 || r.EndTime <= r.StartTime || string.IsNullOrWhiteSpace(r.Location)) throw new BusinessException(400, "Thông tin ca học không hợp lệ."); }
    private static void ValidateArrayJson(string? json) { if (string.IsNullOrWhiteSpace(json)) return; try { using var doc = JsonDocument.Parse(json); if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new Exception(); } catch { throw new BusinessException(400, "GoalsJson và IncludesJson phải là JSON array hợp lệ."); } }
    private static string NormalizeArrayJson(string? json) => string.IsNullOrWhiteSpace(json) ? "[]" : json;
    private static WorkshopDto ToDto(Workshop x) => new(x.Id, x.Code, x.Name, x.ShortName, x.Category, x.BasePrice, x.Duration, x.MaxCapacity, x.Description, x.LeadContent, x.GoalsJson, x.IncludesJson, x.ForWho, x.IsFeatured, x.IsActive, x.InstructorId);
    private static BookingDto ToDto(WorkshopBooking x) => new(x.Id, x.SessionId, x.BookingCode, x.CustomerName, x.TicketCount, x.TotalAmount, x.DepositAmount, x.RemainingAmount, x.BookingStatus, x.HoldExpiresAt, x.CheckedInAt, x.CreatedAt);
    private static SessionDto ToDto(WorkshopSession x) => new(x.Id, x.WorkshopId, x.SessionDate, x.StartTime, x.EndTime, x.Location, x.Capacity, x.BookedSlots, x.SessionStatus);
}
