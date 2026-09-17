using System.Data.Common;
using Confera.Application.Reports;
using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using Confera.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using static Confera.Integration.Tests.BookingTestSupport;

namespace Confera.Integration.Tests;

public sealed class ReportQueryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task RoomValuesCountBookingsOnceAndKeepServiceFreeBookings()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, other) = await SeedAsync(database);
        await using (var db = database.Context())
        {
            db.Add(new Room("No bookings", 50, 1000m));
            db.Add(room.Book(Period(At(11), At(15)), At(9), room.Services.Select(x => x.Id).ToArray(), Rules()));
            db.Add(room.Book(Period(At(15), At(16)), At(9), [room.Services.Single(x => x.Name == "Projector").Id], Rules()));
            db.Add(other.Book(Period(At(11), At(15)), At(9), [], Rules()));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var observer = new ReportReadObserver();
        var report = await Reports(database, observer).GetRoomsAsync(At(0), At(0).AddDays(1), TestContext.Current.CancellationToken);

        Assert.Equal(new[]
        {
            new RoomReportRow(other.Id, "Room B", 1, 14400m, 15050m, 0m, 15050m),
            new RoomReportRow(room.Id, "Room A", 2, 18000m, 10600m, 1300m, 11900m)
        }, report.Items);
        Assert.Single(observer.Commands);
        Assert.Equal(2, observer.ParameterCounts.Single());
        Assert.Contains("GROUP BY", observer.Commands.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FOR UPDATE", observer.Commands.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BothReportsSelectByStartAndKeepTheFullBookingAcrossTheWindowEnd()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, other) = await SeedAsync(database);
        await using (var db = database.Context())
        {
            // Earlier overlapping booking and booking at the exclusive end must not be selected.
            db.Add(other.Book(Period(At(10), At(12)), At(9), other.Services.Select(x => x.Id).ToArray(), Rules()));
            db.Add(room.Book(Period(At(11), At(15)), At(9), room.Services.Select(x => x.Id).ToArray(), Rules()));
            db.Add(other.Book(Period(At(12), At(13)), At(9), other.Services.Select(x => x.Id).ToArray(), Rules()));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var reports = Reports(database);
        var rooms = await reports.GetRoomsAsync(At(11), At(12), TestContext.Current.CancellationToken);
        var services = await reports.GetServicesAsync(At(11), At(12), TestContext.Current.CancellationToken);
        Assert.Equal(new RoomReportRow(room.Id, room.Name, 1, 14400m, 8600m, 800m, 9400m), Assert.Single(rooms.Items));
        Assert.Equal(new[] { new ServiceReportRow("Projector", 1, 500m), new ServiceReportRow("Wi-Fi", 1, 300m) }, services.Items);

        // All three bookings were created at 09:00, but none starts in this window.
        Assert.Empty((await reports.GetRoomsAsync(At(9), At(10), TestContext.Current.CancellationToken)).Items);
        Assert.Empty((await reports.GetServicesAsync(At(9), At(10), TestContext.Current.CancellationToken)).Items);
    }

    [Fact]
    public async Task ReportsPreserveDecimalMoneyAndMicrosecondDurations()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var room = new Room("Fractional", 10, 1000.001m);
        room.SetServices([new("Service", 200.123m)]);
        var start = At(10).AddTicks(1234560);
        var end = start.AddMinutes(30).AddTicks(10);
        await using (var db = database.Context())
        {
            db.Add(room);
            db.Add(room.Book(Period(start, end), At(9), room.Services.Select(x => x.Id).ToArray(), Rules()));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // A one-microsecond report window is valid and includes the complete booking.
        var report = await Reports(database).GetRoomsAsync(start, start.AddTicks(10), TestContext.Current.CancellationToken);
        Assert.Equal(new RoomReportRow(room.Id, room.Name, 1, 1800.000001m, 500.001m, 200.123m, 700.124m), Assert.Single(report.Items));
        var services = await Reports(database).GetServicesAsync(start, start.AddTicks(10), TestContext.Current.CancellationToken);
        Assert.Equal(new ServiceReportRow("Service", 1, 200.123m), Assert.Single(services.Items));
    }

    [Fact]
    public async Task HistoricalReportsSurviveEditsDeletionAndNameReuse()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        await using (var db = database.Context())
        {
            db.Add(room.Book(Period(At(11), At(15)), At(9), room.Services.Select(x => x.Id).ToArray(), Rules()));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = database.Context())
        {
            var current = await db.Rooms.Include(x => x.Services).SingleAsync(x => x.Id == room.Id, TestContext.Current.CancellationToken);
            current.Update(new RoomDetails("Renamed room", 50, 4000m, [new("Equipment", 900m)]));
            db.Add(current.Book(Period(At(15), At(16)), At(9), current.Services.Select(x => x.Id).ToArray(), Rules()));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            current.Delete();
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            var replacement = new Room("Renamed room", 50, 1000m);
            db.Add(replacement);
            db.Add(replacement.Book(Period(At(11), At(12)), At(9), [], Rules()));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await db.PricingRules.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        var reports = Reports(database);
        var rooms = await reports.GetRoomsAsync(At(0), At(0).AddDays(1), TestContext.Current.CancellationToken);
        Assert.Equal(2, rooms.Items.Count);
        Assert.All(rooms.Items, x => Assert.Equal("Renamed room", x.RoomName));
        Assert.Equal(new RoomReportRow(room.Id, "Renamed room", 2, 18000m, 12600m, 1700m, 14300m), rooms.Items[0]);
        Assert.NotEqual(room.Id, rooms.Items[1].RoomId);
        Assert.Equal(1000m, rooms.Items[1].TotalValue);
        var services = await reports.GetServicesAsync(At(0), At(0).AddDays(1), TestContext.Current.CancellationToken);
        Assert.Equal(new[]
        {
            new ServiceReportRow("Equipment", 1, 900m), new ServiceReportRow("Projector", 1, 500m), new ServiceReportRow("Wi-Fi", 1, 300m)
        }, services.Items);
    }

    [Fact]
    public async Task ServiceNamesGroupExactlyAcrossRoomsAndSortByCountThenName()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var first = new Room("First", 10, 1000m);
        first.SetServices([new("Projector", 500m), new("Wi-Fi", 300m)]);
        var second = new Room("Second", 10, 1000m);
        second.SetServices([new("Projector", 600m)]);
        var third = new Room("Third", 10, 1000m);
        third.SetServices([new("projector", 700m), new("Unused", 200m)]);
        await using (var db = database.Context())
        {
            foreach (var room in new[] { first, second, third })
            {
                db.Add(room);
                db.Add(room.Book(Period(At(11), At(12)), At(9), room.Services.Where(x => x.Name != "Unused").Select(x => x.Id).ToArray(), Rules()));
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var observer = new ReportReadObserver();
        var report = await Reports(database, observer).GetServicesAsync(At(0), At(0).AddDays(1), TestContext.Current.CancellationToken);
        Assert.Equal(new[]
        {
            new ServiceReportRow("Projector", 2, 1100m), new ServiceReportRow("Wi-Fi", 1, 300m), new ServiceReportRow("projector", 1, 700m)
        }, report.Items);
        Assert.Single(observer.Commands);
        Assert.Equal(2, observer.ParameterCounts.Single());
        Assert.Contains("GROUP BY", observer.Commands.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BookingPriceSegments", observer.Commands.Single());
        Assert.DoesNotContain("\"RoomServices\"", observer.Commands.Single());
    }

    [Fact]
    public async Task ReportsReturnEveryGroupAndBreakRoomValueTiesById()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var rooms = Enumerable.Range(0, 105).Select(i => new Room($"Room {i:D3}", 10, 1000m)).ToArray();
        await using (var db = database.Context())
        {
            foreach (var room in rooms)
            {
                room.SetServices([new(room.Name, 200m)]);
                db.Add(room);
                db.Add(room.Book(Period(At(11), At(12)), At(9), room.Services.Select(x => x.Id).ToArray(), Rules()));
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var reports = Reports(database);
        var roomReport = await reports.GetRoomsAsync(At(0), At(0).AddDays(1), TestContext.Current.CancellationToken);
        Assert.Equal(rooms.OrderBy(x => x.Id).Select(x => x.Id), roomReport.Items.Select(x => x.RoomId));
        var serviceReport = await reports.GetServicesAsync(At(0), At(0).AddDays(1), TestContext.Current.CancellationToken);
        Assert.Equal(rooms.Select(x => x.Name).Order(StringComparer.Ordinal), serviceReport.Items.Select(x => x.ServiceName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportQueryHonorsCancellation(bool services)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var observer = new ReportReadObserver(pause: true);
        var reports = Reports(database, observer);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task query = services
            ? reports.GetServicesAsync(At(0), At(0).AddDays(1), cancellation.Token)
            : reports.GetRoomsAsync(At(0), At(0).AddDays(1), cancellation.Token);
        await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        Assert.Single(observer.Commands);
        Assert.Empty((await Reports(database).GetRoomsAsync(At(0), At(0).AddDays(1), TestContext.Current.CancellationToken)).Items);
    }

    private static ReportService Reports(TestDatabase database, params IInterceptor[] interceptors) =>
        new(new ReportReader(new BookingContextFactory(database, interceptors)));
}

internal sealed class ReportReadObserver(string? failure = null, bool pause = false) : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];
    public List<int> ParameterCounts { get; } = [];
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        ParameterCounts.Add(command.Parameters.Count);
        if (pause)
        {
            command.CommandText = "SELECT pg_sleep(30)";
            Started.TrySetResult();
        }

        if (failure is not null)
        {
            throw failure switch
            {
                "timeout" => new NpgsqlException("PRIVATE_PROVIDER_DETAILS", new TimeoutException()),
                "schema" => new PostgresException("PRIVATE_SCHEMA_DETAILS", "ERROR", "ERROR", PostgresErrorCodes.UndefinedColumn),
                _ => new ArgumentException("PRIVATE_CONFIGURATION_DETAILS")
            };
        }

        return ValueTask.FromResult(result);
    }
}
