using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HoaSap.DAL;
using HoaSap.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoaSap.Tests;

public sealed class WorkshopLifecycleTests
{
    private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<(ApiFixture Fixture, HttpClient Admin)> StartAsync()
    {
        var fixture = new ApiFixture();
        var client = fixture.CreateClient();
        var response = await client.PostAsync("/api/auth/login", Json(new { email = ApiFixture.AdminEmail, password = ApiFixture.AdminPassword }));
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", document.RootElement.GetProperty("accessToken").GetString());
        return (fixture, client);
    }

    private static async Task<HttpClient> CustomerAsync(ApiFixture fixture)
    {
        var client = fixture.CreateClient();
        var email = $"workshop-{Guid.NewGuid():N}@example.test";
        var response = await client.PostAsync("/api/auth/register", Json(new { fullName = "Workshop Customer", phoneNumber = "09" + Random.Shared.Next(100000000, 999999999), email, password = "Password-123!" }));
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", document.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<(Guid WorkshopId, Guid SessionId, decimal Price)> WorkshopAsync(HttpClient admin, int capacity = 3, decimal price = 100000m, DateTime? date = null)
    {
        var workshop = await admin.PostAsync("/api/workshops", Json(new { code = "W-" + Guid.NewGuid().ToString("N")[..10], name = "Lifecycle Workshop", basePrice = price, duration = 60, maxCapacity = capacity, isActive = true }));
        workshop.EnsureSuccessStatusCode();
        using var wd = JsonDocument.Parse(await workshop.Content.ReadAsStringAsync());
        var workshopId = wd.RootElement.GetProperty("id").GetGuid();
        var sessionDate = (date ?? DateTime.UtcNow.AddDays(3)).Date;
        var session = await admin.PostAsync($"/api/workshops/{workshopId}/sessions", Json(new { sessionDate, startTime = "10:00:00", endTime = "11:00:00", location = "Studio A", capacity }));
        session.EnsureSuccessStatusCode();
        using var sd = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        return (workshopId, sd.RootElement.GetProperty("id").GetGuid(), price);
    }

    private static async Task<(Guid Id, string Status)> BookingAsync(HttpClient customer, Guid sessionId, string key, string name = "Customer", int tickets = 1)
    {
        var response = await customer.PostAsync($"/api/bookings/sessions/{sessionId}", Json(new { ticketCount = tickets, customerName = name, customerPhone = "0901234567", idempotencyKey = key }));
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (document.RootElement.GetProperty("id").GetGuid(), document.RootElement.GetProperty("bookingStatus").GetString()!);
    }

    private static async Task<JsonElement> BookingJsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task StaffCanCreateFutureWorkshopAndSessionWithCapacity()
    {
        var (fixture, admin) = await StartAsync();
        using (fixture) using (admin)
        {
            var created = await WorkshopAsync(admin, capacity: 7);
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
            var workshop = await db.Workshops.SingleAsync(x => x.Id == created.WorkshopId);
            var session = await db.WorkshopSessions.SingleAsync(x => x.Id == created.SessionId);
            Assert.Equal(7, workshop.MaxCapacity);
            Assert.Equal(7, session.Capacity);
            Assert.True(session.SessionDate.Date > DateTime.UtcNow.Date);
            Assert.Equal(0, session.BookedSlots);
            Assert.Equal("Open", session.SessionStatus);
        }
    }

    [Fact]
    public async Task BookingReplayIsIdempotentMismatchConflictsAndCapacityIsNotDoubleConsumed()
    {
        var (fixture, admin) = await StartAsync();
        using (fixture) using (admin)
        {
            var (_, sessionId, _) = await WorkshopAsync(admin, capacity: 1);
            using var customer = await CustomerAsync(fixture);
            const string key = "same-booking-key";
            var first = await BookingAsync(customer, sessionId, key);
            var replay = await customer.PostAsync($"/api/bookings/sessions/{sessionId}", Json(new { ticketCount = 1, customerName = "Customer", customerPhone = "0901234567", idempotencyKey = key }));
            var replayJson = await BookingJsonAsync(replay);
            Assert.Equal(first.Id, replayJson.GetProperty("id").GetGuid());
            var mismatch = await customer.PostAsync($"/api/bookings/sessions/{sessionId}", Json(new { ticketCount = 2, customerName = "Customer", customerPhone = "0901234567", idempotencyKey = key }));
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
            var session = await db.WorkshopSessions.SingleAsync(x => x.Id == sessionId);
            Assert.Equal(1, session.BookedSlots);
            Assert.Equal(1, await db.WorkshopBookings.CountAsync(x => x.SessionId == sessionId));
        }
    }

    [Fact]
    public async Task DifferentCustomersMayReuseTheSameIdempotencyKey()
    {
        var (fixture, admin) = await StartAsync();
        using (fixture) using (admin)
        {
            var (_, sessionId, _) = await WorkshopAsync(admin, capacity: 2);
            using var one = await CustomerAsync(fixture);
            using var two = await CustomerAsync(fixture);
            var first = await BookingAsync(one, sessionId, "shared-customer-key", "One");
            var second = await BookingAsync(two, sessionId, "shared-customer-key", "Two");
            Assert.NotEqual(first.Id, second.Id);
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
            Assert.Equal(2, await db.WorkshopBookings.CountAsync(x => x.SessionId == sessionId));
            Assert.Equal(2, (await db.WorkshopSessions.SingleAsync(x => x.Id == sessionId)).BookedSlots);
        }
    }

    [Fact]
    public async Task ActiveBookingBlocksSessionTimeOrLocationUpdateAndRosterRequiresStaff()
    {
        var (fixture, admin) = await StartAsync();
        using (fixture) using (admin)
        {
            var (workshopId, sessionId, _) = await WorkshopAsync(admin);
            using var customer = await CustomerAsync(fixture);
            await BookingAsync(customer, sessionId, "update-block");
            var changed = await admin.PutAsync($"/api/workshops/sessions/{sessionId}", Json(new { sessionDate = DateTime.UtcNow.AddDays(4).Date, startTime = "11:00:00", endTime = "12:00:00", location = "Studio B", capacity = 3 }));
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
            var roster = await admin.GetAsync($"/api/workshops/sessions/{sessionId}/roster");
            roster.EnsureSuccessStatusCode();
            using var rosterJson = JsonDocument.Parse(await roster.Content.ReadAsStringAsync());
            Assert.Single(rosterJson.RootElement.EnumerateArray());
            Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.CreateClient().GetAsync("/api/bookings")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/workshops/{workshopId}/sessions")).StatusCode);
        }
    }

    [Fact]
    public async Task PaidBookingCannotCheckInOrNoShowBeforeStartThenCheckInAttendsAfterDbTimeAdjustment()
    {
        var (fixture, admin) = await StartAsync();
        using (fixture) using (admin)
        {
            var (_, sessionId, price) = await WorkshopAsync(admin, capacity: 2);
            using var customer = await CustomerAsync(fixture);
            var booking = await BookingAsync(customer, sessionId, "paid-checkin");
            var paid = await admin.PostAsync($"/api/payments/booking/{booking.Id}", Json(new { transactionCode = "PAY-" + Guid.NewGuid().ToString("N"), paymentMethod = "Cash", amount = price }));
            paid.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/bookings/{booking.Id}/check-in", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/bookings/{booking.Id}/no-show", null)).StatusCode);
            using (var scope = fixture.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
                var session = await db.WorkshopSessions.SingleAsync(x => x.Id == sessionId);
                var vn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));
                session.SessionDate = vn.Date;
                session.StartTime = vn.TimeOfDay - TimeSpan.FromMinutes(30);
                session.EndTime = vn.TimeOfDay + TimeSpan.FromMinutes(30);
                await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/bookings/{booking.Id}/check-in", null)).StatusCode);
            using var verify = fixture.Services.CreateScope();
            var saved = await verify.ServiceProvider.GetRequiredService<HoaSapDbContext>().WorkshopBookings.SingleAsync(x => x.Id == booking.Id);
            Assert.Equal("attended", saved.BookingStatus);
            Assert.NotNull(saved.CheckedInAt);
        }
    }

    [Fact]
    public async Task PaidCancellationCreatesOneFullRefundAndRefundReplayDoesNotDuplicate()
    {
        var (fixture, admin) = await StartAsync();
        using (fixture) using (admin)
        {
            var (_, sessionId, price) = await WorkshopAsync(admin);
            using var customer = await CustomerAsync(fixture);
            var booking = await BookingAsync(customer, sessionId, "cancel-refund");
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/payments/booking/{booking.Id}", Json(new { transactionCode = "PAY-" + Guid.NewGuid().ToString("N"), paymentMethod = "Cash", amount = price }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await customer.PostAsync($"/api/bookings/{booking.Id}/cancel", Json("customer request"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await customer.PostAsync($"/api/bookings/{booking.Id}/cancel", Json("customer request"))).StatusCode);
            using (var scope = fixture.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
                var refunds = await db.Refunds.Where(x => x.BookingId == booking.Id).ToListAsync();
                Assert.Single(refunds);
                Assert.Equal(price, refunds[0].RefundAmount);
                Assert.Equal("Requested", refunds[0].RefundStatus);
                Assert.Equal(price, (await db.Payments.Where(x => x.BookingId == booking.Id).Select(x => x.Amount).ToListAsync()).Sum());
            }
            var list = await admin.GetAsync("/api/refunds");
            list.EnsureSuccessStatusCode();
            using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var refundId = listJson.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("bookingId").GetGuid() == booking.Id).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/refunds/{refundId}/approve", null)).StatusCode);
            var processed = await admin.PostAsync($"/api/refunds/{refundId}/process", Json(new { transactionCode = "REF-" + Guid.NewGuid().ToString("N") }));
            processed.EnsureSuccessStatusCode();
            using var verify = fixture.Services.CreateScope();
            var saved = await verify.ServiceProvider.GetRequiredService<HoaSapDbContext>().Refunds.SingleAsync(x => x.Id == refundId);
            Assert.Equal("Processed", saved.RefundStatus);
            Assert.Equal(price, (await verify.ServiceProvider.GetRequiredService<HoaSapDbContext>().Refunds.Where(x => x.BookingId == booking.Id).Select(x => x.RefundAmount).ToListAsync()).Sum());
        }
    }

    [Fact]
    public async Task StaffCannotCloseSessionBeforeItsEnd()
    {
        var (fixture, admin) = await StartAsync();
        using (fixture) using (admin)
        {
            var (_, sessionId, _) = await WorkshopAsync(admin);
            var response = await admin.PostAsync($"/api/workshops/sessions/{sessionId}/close", null);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var scope = fixture.Services.CreateScope();
            var session = await scope.ServiceProvider.GetRequiredService<HoaSapDbContext>().WorkshopSessions.SingleAsync(x => x.Id == sessionId);
            Assert.NotEqual("Closed", session.SessionStatus);
        }
    }
}
