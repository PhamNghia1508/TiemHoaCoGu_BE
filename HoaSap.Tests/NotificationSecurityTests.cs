using System.Text.Json;
using HoaSap.API.Services;
using HoaSap.DAL;
using HoaSap.DAL.Notifications;
using HoaSap.Domain;
using HoaSap.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace HoaSap.Tests;

public sealed class NotificationSecurityTests
{
    private static HoaSapDbContext Db(SqliteConnection connection) => new(new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite(connection).Options);

    [Fact]
    public async Task StagingOnlyEmitsStatusTransitionsWithRealDestinations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Db(connection); await db.Database.EnsureCreatedAsync();
        var customer = new Customer { Email = "customer@example.test", PhoneNumber = "+84901234567", FullName = "Customer" };
        var order = new Order { CustomerId = customer.Id, OrderCode = "O-1", OrderStatus = "Confirmed", Note = "initial" };
        db.Customers.Add(customer); db.Orders.Add(order); await db.SaveChangesAsync();
        db.Entry(order).Property(x => x.Note).CurrentValue = "changed"; await db.SaveChangesAsync();
        Assert.Equal(2, await db.Set<NotificationOutbox>().CountAsync());
        db.Entry(order).Property(x => x.OrderStatus).CurrentValue = "Delivered"; await db.SaveChangesAsync();
        var rows = await db.Set<NotificationOutbox>().Where(x => x.EventType == "OrderChanged").ToListAsync();
        Assert.Equal(4, rows.Count); Assert.Contains(rows, x => x.Channel == NotificationChannel.Email && x.Destination == customer.Email); Assert.Contains(rows, x => x.Channel == NotificationChannel.Sms && x.Destination == customer.PhoneNumber);
    }

    [Fact]
    public async Task PasswordResetStagingDoesNotPersistRawToken()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Db(connection); await db.Database.EnsureCreatedAsync();
        var customer = new Customer { Email = "customer@example.test", PhoneNumber = "+84901234567", FullName = "Customer" }; db.Customers.Add(customer);
        NotificationOutboxStaging.StagePasswordReset(db, customer, "protected-value", "https://client.example/reset?token=protected-value"); await db.SaveChangesAsync();
        var row = await db.Set<NotificationOutbox>().SingleAsync(); Assert.DoesNotContain("raw-secret", row.PayloadJson); Assert.Contains("recoveryUrl", row.PayloadJson); using var json = JsonDocument.Parse(row.PayloadJson); Assert.Equal("protected-value", json.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public void DisabledTransportsNeverAdvertiseEnabled()
    {
        var options = Options.Create(new NotificationOptions { EmailEnabled = false, SmsEnabled = false });
        Assert.False(new SmtpNotificationTransport(options).Enabled);
        Assert.False(new HttpsSmsNotificationTransport(new StubHttpClientFactory(), options).Enabled);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
}
