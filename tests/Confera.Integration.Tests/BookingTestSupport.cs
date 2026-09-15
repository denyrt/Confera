using System.Collections.Concurrent;
using System.Data.Common;
using Confera.Application.Bookings;
using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using Confera.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Confera.Integration.Tests;

internal static class BookingTestSupport
{
    internal static DateTime At(int hour) => new(2030, 1, 15, hour, 0, 0, DateTimeKind.Utc);

    internal static async Task<(Room Room, Room Other)> SeedAsync(TestDatabase database)
    {
        var room = new Room("Room A", 50, 2000m);
        room.SetServices([new("Projector", 500m), new("Wi-Fi", 300m)]);
        var other = new Room("Room B", 100, 3500m);
        other.SetServices([new("Projector", 500m)]);
        await using var db = database.Context();
        db.AddRange(room, other);
        db.AddRange(Rules());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (room, other);
    }

    internal static BookingPricingRule[] Rules() =>
    [
        new("Morning", "Morning", new TimeOnly(6, 0), new TimeOnly(9, 0), 0.9m, 0),
        new("Standard", "Standard", new TimeOnly(9, 0), new TimeOnly(18, 0), 1m, 1),
        new("Evening", "Evening", new TimeOnly(18, 0), new TimeOnly(23, 0), 0.8m, 2),
        new("Peak", "Peak", new TimeOnly(12, 0), new TimeOnly(14, 0), 1.15m, 3)
    ];

    internal static CreateBookingCommand Command(Room room) =>
        new(room.Id, At(11), At(15), room.Services.Select(x => x.Id).ToArray());

    internal static CreateBookingService Service(TestDatabase database, params IInterceptor[] interceptors) =>
        new(new BookingStore(new BookingContextFactory(database, interceptors)), new BookingClock(At(9)));

    internal static async Task WaitForBlockedConnectionAsync(TestDatabase database, int pid, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var query = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid)) > 0", connection);
        query.Parameters.AddWithValue("pid", pid);
        using var poll = new PeriodicTimer(TimeSpan.FromMilliseconds(20));

        // Observe PostgreSQL's actual wait state; elapsed time never establishes the race.
        while (!(bool)(await query.ExecuteScalarAsync(cancellationToken))!)
        {
            await poll.WaitForNextTickAsync(cancellationToken);
        }
    }
}

internal sealed class BookingClock(DateTime now) : TimeProvider
{
    public DateTime Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => new(Now);
}

internal sealed class BookingContextFactory(TestDatabase database, params IInterceptor[] interceptors)
    : IDbContextFactory<ConferaDbContext>
{
    public ConferaDbContext CreateDbContext() => new(new DbContextOptionsBuilder<ConferaDbContext>(
        PersistenceConfiguration.Options(database.ConnectionString)).AddInterceptors(interceptors).Options);
}

internal sealed class RoomLockObserver(int participants = 1) : DbCommandInterceptor
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;
    public TaskCompletionSource<int> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ConcurrentBag<int> ConnectionIds { get; } = [];

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
        {
            var pid = ((NpgsqlConnection)command.Connection!).ProcessID;
            ConnectionIds.Add(pid);
            Started.TrySetResult(pid);
            if (Interlocked.Increment(ref _arrivals) == participants)
            {
                _ready.TrySetResult();
            }

            await _ready.Task.WaitAsync(cancellationToken);
        }

        return result;
    }
}
