using System.Text.Json;
using HoaSap.API.Services;
using HoaSap.DAL;
using HoaSap.DAL.Notifications;
using HoaSap.Domain;
using HoaSap.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Reflection;
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
        var row = await db.Set<NotificationOutbox>().AsNoTracking().SingleAsync(); Assert.DoesNotContain("raw-secret", row.PayloadJson); Assert.Contains("recoveryUrl", row.PayloadJson); using var json = JsonDocument.Parse(row.PayloadJson); Assert.Equal("protected-value", json.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public void DisabledTransportsNeverAdvertiseEnabled()
    {
        var options = Options.Create(new NotificationOptions { EmailEnabled = false, SmsEnabled = false });
        Assert.False(new SmtpNotificationTransport(options).Enabled);
        Assert.False(new HttpsSmsNotificationTransport(new StubHttpClientFactory(), options).Enabled);
    }

    [Fact]
    public async Task DeliveryTimeoutBecomesUnknownAndIsNotRetried()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Db(connection); await db.Database.EnsureCreatedAsync();
        var row = new NotificationOutbox { Channel = NotificationChannel.Sms, Destination = "+84901234567", PayloadJson = "{}" };
        db.Add(row); await db.SaveChangesAsync();
        var transport = new ThrowingTransport(new OperationCanceledException("provider timeout"));
        await RunWorker(db, transport);
        await RunWorker(db, transport);
        var saved = await db.Set<NotificationOutbox>().AsNoTracking().SingleAsync();
        Assert.Equal(NotificationStatus.Unknown, saved.Status);
        Assert.Equal(1, transport.Calls);
        Assert.Contains("unknown", saved.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpiredProcessingLeaseIsUnknownAndNeverReclaimed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Db(connection); await db.Database.EnsureCreatedAsync();
        var row = new NotificationOutbox { Channel = NotificationChannel.Sms, Destination = "+84901234567", Status = NotificationStatus.Processing, LeasedUntilUtc = DateTime.UtcNow.AddMinutes(-1), LeaseOwner = "dead-worker" };
        db.Add(row); await db.SaveChangesAsync();
        var transport = new ThrowingTransport(null);
        await RunWorker(db, transport);
        await RunWorker(db, transport);
        var saved = await db.Set<NotificationOutbox>().AsNoTracking().SingleAsync();
        Assert.Equal(NotificationStatus.Unknown, saved.Status);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task DefinitePreAcceptanceFailureCanRetryWithSameMessage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Db(connection); await db.Database.EnsureCreatedAsync();
        var row = new NotificationOutbox { Channel = NotificationChannel.Sms, Destination = "+84901234567" };
        db.Add(row); await db.SaveChangesAsync();
        var transport = new ThrowingTransport(new NotificationDeliveryException("definitely not accepted", false));
        await RunWorker(db, transport);
        Assert.Equal(NotificationStatus.Pending, (await db.Set<NotificationOutbox>().AsNoTracking().SingleAsync()).Status);
        await db.Set<NotificationOutbox>().ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        await RunWorker(db, transport);
        Assert.Equal(2, transport.Calls);
        Assert.Equal(2, (await db.Set<NotificationOutbox>().AsNoTracking().SingleAsync()).Attempts);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    public async Task GenericSmsBridgeErrorIsAmbiguousWithoutProviderContract(int status)
    {
        var handler = new RecordingHandler(status);
        var transport = new HttpsSmsNotificationTransport(new RecordingFactory(handler), Options.Create(new NotificationOptions
        {
            SmsEnabled = true,
            Sms = new SmsOptions { Endpoint = "https://sms.example.test/send", ApiKey = "test-only" }
        }));
        var row = new NotificationOutbox { Destination = "+84901234567", PayloadJson = "{\"code\":\"QA\",\"status\":\"Confirmed\"}" };
        for (var i = 0; i < 2; i++)
        {
            var error = await Assert.ThrowsAsync<NotificationDeliveryException>(() => transport.SendAsync(row, CancellationToken.None));
            Assert.True(error.IsAmbiguous);
        }
        Assert.Equal(new[] { row.Id.ToString("N"), row.Id.ToString("N") }, handler.Keys);
    }

    private sealed class RecordingHandler(int status) : HttpMessageHandler
    {
        public List<string> Keys { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)status));
        }
    }
    private sealed class RecordingFactory(RecordingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static async Task RunWorker(HoaSapDbContext db, INotificationTransport transport)
    {
        // A separate context shares the externally-owned SQLite connection. Worker scope
        // disposal must not dispose the caller context or leave its tracked entity stale.
        using var provider = new ServiceCollection().AddScoped(_ => Db((SqliteConnection)db.Database.GetDbConnection())).BuildServiceProvider();
        var worker = new NotificationWorker(provider.GetRequiredService<IServiceScopeFactory>(), new[] { transport },
            Options.Create(new NotificationOptions { SmsEnabled = true, LeaseSeconds = 60 }), NullLogger<NotificationWorker>.Instance);
        var method = typeof(NotificationWorker).GetMethod("ProcessBatch", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(worker, new object[] { CancellationToken.None })!;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }

    private sealed class ThrowingTransport(Exception? error) : INotificationTransport
    {
        public NotificationChannel Channel => NotificationChannel.Sms;
        public bool Enabled => true;
        public int Calls { get; private set; }
        public Task SendAsync(NotificationOutbox message, CancellationToken ct)
        {
            Calls++;
            if (error is not null) return Task.FromException(error);
            return Task.CompletedTask;
        }
    }
}
