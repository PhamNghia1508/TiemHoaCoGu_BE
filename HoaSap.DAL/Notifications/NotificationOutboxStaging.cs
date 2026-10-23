using System.Text.Json;
using HoaSap.Domain;
using HoaSap.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace HoaSap.DAL.Notifications;

public static class NotificationOutboxStaging
{
    public static void Stage(ChangeTracker tracker)
    {
        var existing = tracker.Entries<NotificationOutbox>().Select(x => x.Entity)
            .Select(x => (x.EventType, x.CustomerId, x.Channel, x.Destination, x.PayloadJson)).ToHashSet();
        foreach (var entry in tracker.Entries().Where(x => x.Entity is Order or WorkshopBooking && IsStateChange(x)).ToList())
        {
            var (type, customerId, code, status, email, phone) = entry.Entity switch
            {
                Order o => ("OrderChanged", o.CustomerId, o.OrderCode, o.OrderStatus, FindCustomerEmail(tracker, o.CustomerId) ?? OrderContact(o.ShippingAddress, "ReceiverEmail"), FindCustomerPhone(tracker, o.CustomerId) ?? OrderContact(o.ShippingAddress, "ReceiverPhone")),
                WorkshopBooking w => ("WorkshopBookingChanged", w.CustomerId, w.BookingCode, w.BookingStatus, string.IsNullOrWhiteSpace(w.CustomerEmail) ? FindCustomerEmail(tracker, w.CustomerId) : w.CustomerEmail, string.IsNullOrWhiteSpace(w.CustomerPhone) ? FindCustomerPhone(tracker, w.CustomerId) : w.CustomerPhone),
                _ => ("", (Guid?)null, "", "", null, null)
            };
            if (string.IsNullOrWhiteSpace(type)) continue;
            var paymentStatus = entry.Entity is Order order ? order.PaymentStatus : null;
            var payload = JsonSerializer.Serialize(new { entity = type, code, status, paymentStatus });
            Add(tracker, type, customerId, NotificationChannel.Email, email, payload, existing);
            Add(tracker, type, customerId, NotificationChannel.Sms, phone, payload, existing);
        }
    }

    public static void StagePasswordReset(HoaSapDbContext db, Customer customer, string protectedToken, string recoveryUrl)
    {
        var payload = JsonSerializer.Serialize(new { token = protectedToken, recoveryUrl });
        db.Set<NotificationOutbox>().Add(new NotificationOutbox { EventType = "PasswordReset", CustomerId = customer.Id, Channel = NotificationChannel.Email, Destination = customer.Email, Subject = "Đặt lại mật khẩu", PayloadJson = payload });
    }

    private static bool IsStateChange(EntityEntry entry)
    {
        if (entry.State == EntityState.Added) return true;
        if (entry.State != EntityState.Modified) return false;
        if (entry.Entity is Order && entry.Property(nameof(Order.PaymentStatus)).IsModified && !Equals(entry.Property(nameof(Order.PaymentStatus)).OriginalValue, entry.Property(nameof(Order.PaymentStatus)).CurrentValue)) return true;
        var property = entry.Metadata.FindProperty(entry.Entity is Order ? nameof(Order.OrderStatus) : nameof(WorkshopBooking.BookingStatus));
        return property is not null && entry.Property(property.Name).IsModified && !Equals(entry.Property(property.Name).OriginalValue, entry.Property(property.Name).CurrentValue);
    }

    private static void Add(ChangeTracker tracker, string type, Guid? customerId, NotificationChannel channel, string? destination, string payload, HashSet<(string, Guid?, NotificationChannel, string?, string)> existing)
    {
        if (string.IsNullOrWhiteSpace(destination) || existing.Contains((type, customerId, channel, destination, payload))) return;
        tracker.Context.Set<NotificationOutbox>().Add(new NotificationOutbox { EventType = type, CustomerId = customerId, Channel = channel, Destination = destination.Trim(), Subject = type, PayloadJson = payload });
    }

    private static string? FindCustomerEmail(ChangeTracker tracker, Guid? id) => id is null ? null : tracker.Context.Set<Customer>().Local.FirstOrDefault(x => x.Id == id)?.Email ?? (id is null ? null : tracker.Context.Set<Customer>().AsNoTracking().Where(x => x.Id == id).Select(x => x.Email).FirstOrDefault());
    private static string? FindCustomerPhone(ChangeTracker tracker, Guid? id) => id is null ? null : tracker.Context.Set<Customer>().Local.FirstOrDefault(x => x.Id == id)?.PhoneNumber ?? (id is null ? null : tracker.Context.Set<Customer>().AsNoTracking().Where(x => x.Id == id).Select(x => x.PhoneNumber).FirstOrDefault());
    private static string? OrderContact(string address, string field)
    {
        var prefix = field + ": ";
        var segment = address.Split(';').Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
        var value = segment?[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
