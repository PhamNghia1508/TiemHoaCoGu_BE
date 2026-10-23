using System.Data;
using HoaSap.BLL;
using HoaSap.BLL.Workshops;
using HoaSap.DAL;
using HoaSap.DAL.Auditing;
using HoaSap.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace HoaSap.Tests;

/// <summary>
/// SQL Server evidence for workshop booking races. Each contender owns a DbContext
/// and WorkshopService; no process-local lock/mutex is used to serialize the race.
/// </summary>
public sealed class SqlServerWorkshopConcurrencyTests
{
    private readonly ITestOutputHelper _output;
    public SqlServerWorkshopConcurrencyTests(ITestOutputHelper output) => _output = output;

    private sealed class FixedActor(Guid id, string role, string traceId) : IAuditActor
    {
        public Guid? ActorId { get; } = id;
        public string? Role { get; } = role;
        public string? TraceId { get; } = traceId;
    }

    private sealed record Seed(Guid SessionId, Guid WorkshopId, Guid FirstCustomerId, Guid SecondCustomerId);
    private sealed record Attempt(bool Succeeded, Guid? BookingId, Exception? Error)
    {
        public bool IsExpectedFailure => Error is BusinessException b && b.StatusCode is 400 or 409
            || Error is DbUpdateException
            || IsDeadlock(Error);
    }

    private sealed class AsyncGate
    {
        private readonly TaskCompletionSource<bool> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public Task WaitAsync()
        {
            if (Interlocked.Increment(ref _arrivals) == 2) _released.TrySetResult(true);
            return _released.Task;
        }
    }

    private sealed class SqlFixture : IAsyncDisposable
    {
        private readonly SqlConnectionStringBuilder _builder;
        public string ConnectionString => _builder.ConnectionString;
        public string DatabaseName => _builder.InitialCatalog;
        public FixedActor Actor { get; } = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "WorkshopIntegrationTest", "sql-workshop-concurrency");

        private SqlFixture(SqlConnectionStringBuilder builder) => _builder = builder;

        public static async Task<SqlFixture> CreateAsync(ITestOutputHelper output)
        {
            var configured = Environment.GetEnvironmentVariable("HOASAP_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("HOASAP_TEST_SQLSERVER is required.");
            var fixture = new SqlFixture(new SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = $"HoaSapSqlWorkshop_{Guid.NewGuid():N}"
            });
            try
            {
                await using var db = fixture.CreateContext();
                await db.Database.MigrateAsync();
                var version = await db.Database.SqlQueryRaw<string>("SELECT @@VERSION AS [Value]").SingleAsync();
                output.WriteLine($"SQL Server @@VERSION ({fixture.DatabaseName}): {version}");
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public HoaSapDbContext CreateContext(IAuditActor? actor = null)
        {
            var options = new DbContextOptionsBuilder<HoaSapDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;
            return new HoaSapDbContext(options, actor ?? Actor);
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            try { await db.Database.EnsureDeletedAsync(); }
            finally
            {
                using var connection = new SqlConnection(ConnectionString);
                SqlConnection.ClearPool(connection);
            }
        }
    }

    [SqlServerFact]
    public async Task LastSeatRace_IndependentContextsCannotOversell_AndReplayIsIdempotent()
    {
        await using var fixture = await SqlFixture.CreateAsync(_output);
        await using var seedDb = fixture.CreateContext();
        var seed = await SeedAsync(seedDb);
        var gate = new AsyncGate();

        var attempts = await Task.WhenAll(
            HoldOnIndependentContextAsync(fixture, seed.SessionId, seed.FirstCustomerId, "race-one", "One", gate),
            HoldOnIndependentContextAsync(fixture, seed.SessionId, seed.SecondCustomerId, "race-two", "Two", gate));

        Assert.Single(attempts, x => x.Succeeded);
        Assert.All(attempts, x => Assert.True(x.Succeeded || x.IsExpectedFailure, x.Error?.ToString()));
        var winner = Assert.Single(attempts, x => x.Succeeded);
        Assert.NotNull(winner.BookingId);

        // Retry the winning request through a fresh context/service, not the winner's tracked instance.
        await using (var replayDb = fixture.CreateContext())
        {
            var replayService = new WorkshopService(replayDb);
            var replay = await replayService.HoldBookingAsync(seed.SessionId, Request(winner == attempts[0] ? "race-one" : "race-two", winner == attempts[0] ? "One" : "Two"), winner == attempts[0] ? seed.FirstCustomerId : seed.SecondCustomerId);
            Assert.Equal(winner.BookingId, replay.Id);
        }

        await using var verify = fixture.CreateContext();
        var session = await verify.WorkshopSessions.AsNoTracking().SingleAsync(x => x.Id == seed.SessionId);
        Assert.Equal(1, session.BookedSlots);
        Assert.Equal("Full", session.SessionStatus);
        Assert.Equal(1, await verify.WorkshopBookings.CountAsync(x => x.SessionId == seed.SessionId));
        Assert.Equal(1, await verify.WorkshopBookings.CountAsync(x => x.SessionId == seed.SessionId && x.BookingStatus != "expired" && x.BookingStatus != "cancelled"));

        var sessionAudits = await verify.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == "WorkshopSession" && x.EntityId == seed.SessionId.ToString() && x.Action == "Modified")
            .ToListAsync();
        // One successful seat mutation only; the idempotent replay must not create another mutation audit.
        Assert.Single(sessionAudits);
        Assert.Equal(0, ReadInt(sessionAudits[0].OldValuesJson, "BookedSlots"));
        Assert.Equal(1, ReadInt(sessionAudits[0].NewValuesJson, "BookedSlots"));
    }

    [SqlServerFact]
    public async Task RepeatedCancellation_ReleasesHeldSeatsOnce_AndWritesOneReleaseAudit()
    {
        await using var fixture = await SqlFixture.CreateAsync(_output);
        await using var seedDb = fixture.CreateContext();
        var seed = await SeedAsync(seedDb, capacity: 1);
        var service = new WorkshopService(seedDb);
        var booking = await service.HoldBookingAsync(seed.SessionId, Request("cancel-once", "One"), seed.FirstCustomerId);

        await using (var cancelDb = fixture.CreateContext())
        {
            var cancelService = new WorkshopService(cancelDb);
            await cancelService.CancelBookingAsync(booking.Id, seed.FirstCustomerId, staff: false, reason: "customer request");
        }
        await using (var replayDb = fixture.CreateContext())
        {
            var replayService = new WorkshopService(replayDb);
            await replayService.CancelBookingAsync(booking.Id, seed.FirstCustomerId, staff: false, reason: "customer request");
        }

        await using var verify = fixture.CreateContext();
        var session = await verify.WorkshopSessions.AsNoTracking().SingleAsync(x => x.Id == seed.SessionId);
        Assert.Equal(0, session.BookedSlots);
        Assert.Equal("Open", session.SessionStatus);
        Assert.Equal("cancelled", await verify.WorkshopBookings.Where(x => x.Id == booking.Id).Select(x => x.BookingStatus).SingleAsync());
        var audits = await verify.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == "WorkshopSession" && x.EntityId == seed.SessionId.ToString() && x.Action == "Modified")
            .ToListAsync();
        Assert.Equal(2, audits.Count);
        // SQL row order is unspecified without ORDER BY; identify the actual
        // transitions instead of assuming a GUID-index scan is chronological.
        Assert.Single(audits, x => ReadInt(x.OldValuesJson, "BookedSlots") == 0 && ReadInt(x.NewValuesJson, "BookedSlots") == 1);
        Assert.Single(audits, x => ReadInt(x.OldValuesJson, "BookedSlots") == 1 && ReadInt(x.NewValuesJson, "BookedSlots") == 0);
    }

    private static async Task<Seed> SeedAsync(HoaSapDbContext db, int capacity = 1)
    {
        var workshop = new Workshop { Code = $"SQL-{Guid.NewGuid():N}", Name = "SQL race workshop", BasePrice = 100m, Duration = 60, MaxCapacity = capacity, IsActive = true };
        var session = new WorkshopSession
        {
            WorkshopId = workshop.Id, SessionDate = DateTime.UtcNow.AddDays(2).Date,
            StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(11, 0, 0),
            Location = "SQL test", Capacity = capacity, SessionStatus = "Open"
        };
        var one = new Customer { Email = $"sql-one-{Guid.NewGuid():N}@example.test", FullName = "One", PhoneNumber = "0900000001", PasswordHash = "test" };
        var two = new Customer { Email = $"sql-two-{Guid.NewGuid():N}@example.test", FullName = "Two", PhoneNumber = "0900000002", PasswordHash = "test" };
        db.AddRange(workshop, session, one, two);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new(session.Id, workshop.Id, one.Id, two.Id);
    }

    private static BookingRequest Request(string key, string name) => new()
    {
        TicketCount = 1, CustomerName = name, CustomerPhone = "0901234567", IdempotencyKey = key
    };

    private static async Task<Attempt> HoldOnIndependentContextAsync(SqlFixture fixture, Guid sessionId, Guid customerId, string key, string name, AsyncGate gate)
    {
        await using var db = fixture.CreateContext();
        var service = new WorkshopService(db);
        await gate.WaitAsync();
        try
        {
            var booking = await service.HoldBookingAsync(sessionId, Request(key, name), customerId);
            return new(true, booking.Id, null);
        }
        catch (Exception ex) when (ex is BusinessException || ex is DbUpdateException || IsDeadlock(ex))
        {
            return new(false, null, ex);
        }
    }

    private static bool IsDeadlock(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sql && sql.Number == 1205) return true;
        return false;
    }

    private static int ReadInt(string json, string property)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty(property).GetInt32();
    }
}
