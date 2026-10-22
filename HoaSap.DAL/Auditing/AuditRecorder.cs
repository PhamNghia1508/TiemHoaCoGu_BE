using System.Text.Json;
using HoaSap.Domain;
using HoaSap.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace HoaSap.DAL.Auditing;

internal static class AuditRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<Type, string[]> Fields = new Dictionary<Type, string[]>
    {
        [typeof(Product)] = ["CategoryId", "Code", "Name", "Price", "OriginalPrice", "Badge", "Section", "IsCustomizable", "IsPreOrder", "IsActive"],
        [typeof(Inventory)] = ["ProductId", "QuantityAvailable", "QuantityReserved", "LastUpdated"],
        [typeof(Voucher)] = ["Code", "DiscountType", "DiscountValue", "MinOrderAmount", "MaxDiscountAmount", "StartDate", "EndDate", "UsageLimit", "UsedCount", "IsActive"],
        [typeof(Order)] = ["OrderCode", "VoucherId", "SubTotal", "DiscountAmount", "ShippingFee", "TotalAmount", "OrderStatus", "PaymentStatus"],
        [typeof(Delivery)] = ["OrderId", "DeliveryCode", "ShipperUnit", "ShippingCost", "DeliveryStatus", "ShippedAt", "DeliveredAt"],
        [typeof(WorkshopSession)] = ["WorkshopId", "SessionDate", "StartTime", "EndTime", "Location", "Capacity", "BookedSlots", "SessionStatus"],
        [typeof(WorkshopBooking)] = ["SessionId", "BookingCode", "TicketCount", "TotalAmount", "DepositAmount", "RemainingAmount", "BookingStatus", "HoldExpiresAt", "CheckedInAt", "CreatedAt"],
        [typeof(Payment)] = ["OrderId", "BookingId", "TransactionCode", "PaymentType", "PaymentMethod", "Amount", "PaymentStatus", "PaidAt"],
        [typeof(Refund)] = ["OrderId", "BookingId", "TransactionCode", "RefundAmount", "RefundStatus", "RequestedAt", "ProcessedAt"]
    };

    public static List<AuditLog> Capture(ChangeTracker tracker, IAuditActor? actor)
    {
        var result = new List<AuditLog>();
        foreach (var entry in tracker.Entries<AuditLog>())
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("AuditLog rows cannot be updated or deleted through the DbContext.");

        foreach (var entry in tracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (!Fields.TryGetValue(entry.Entity.GetType(), out var fields)) continue;
            var oldValues = entry.State == EntityState.Added ? new Dictionary<string, object?>() : Values(entry, fields, false);
            var newValues = entry.State == EntityState.Deleted ? new Dictionary<string, object?>() : Values(entry, fields, true);
            result.Add(new AuditLog
            {
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = Convert.ToString(entry.Property("Id").CurrentValue ?? entry.Property("Id").OriginalValue) ?? "",
                Action = entry.State.ToString(),
                OldValuesJson = JsonSerializer.Serialize(oldValues, JsonOptions),
                NewValuesJson = JsonSerializer.Serialize(newValues, JsonOptions),
                OccurredAtUtc = DateTime.UtcNow,
                ActorId = actor?.ActorId,
                ActorRole = actor?.Role,
                TraceId = actor?.TraceId
            });
        }
        return result;
    }

    private static Dictionary<string, object?> Values(EntityEntry entry, IEnumerable<string> fields, bool current)
        => fields.ToDictionary(f => f, f => (current ? entry.Property(f).CurrentValue : entry.Property(f).OriginalValue));
}
