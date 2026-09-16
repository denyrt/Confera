using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Confera.Integration.Tests;

public sealed class PersistenceTests(PostgresFixture postgres)
{
    private static DateTime At(int hour) => new(2026, 9, 15, hour, 0, 0, DateTimeKind.Utc);
    private static BookingPricingRule[] Rules =>
    [
        new("Standard", "Standard", new TimeOnly(9, 0), new TimeOnly(18, 0), 1m, 1),
        new("Peak", "Peak", new TimeOnly(12, 0), new TimeOnly(14, 0), 1.15m, 3)
    ];

    [Fact]
    public async Task FiniteDateTimeLimitsRoundTripWithoutInfinityConversion()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var minimum = new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var maximum = new DateTime(DateTime.MaxValue.Ticks - 9, DateTimeKind.Utc);
        var room = new Room("Finite", 1, 1000m);
        BookingPricingRule[] rules = [new("Night", "Night", new TimeOnly(22, 0), new TimeOnly(6, 0), 1m, 0)];
        var first = room.Book(minimum, minimum.AddMinutes(30), minimum, [], rules);
        var last = room.Book(maximum.AddMinutes(-30), maximum, maximum.AddMinutes(-30), [], rules);
        await using (var db = database.Context())
        {
            db.Add(room);
            db.AddRange(first, last);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reloaded = database.Context();
        var bookings = await reloaded.Bookings.OrderBy(x => x.StartsAtUtc).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(minimum, bookings[0].StartsAtUtc);
        Assert.Equal(maximum, bookings[1].EndsAtUtc);
        Assert.All(bookings, x => Assert.Equal(DateTimeKind.Utc, x.CreatedAtUtc.Kind));
        Assert.True(await database.ScalarAsync<bool>("SELECT bool_and(isfinite(\"StartsAtUtc\") AND isfinite(\"EndsAtUtc\") AND isfinite(\"CreatedAtUtc\")) FROM \"Bookings\""));
    }

    [Fact]
    public async Task RealMigrationsAreRepeatableAndMatchModel()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var db = database.Context();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.Rooms.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("btree_gist", await database.ScalarAsync<string>("SELECT extname FROM pg_extension WHERE extname='btree_gist'"));
        Assert.Equal("x", await database.ScalarAsync<string>("SELECT contype::text FROM pg_constraint WHERE conname='EX_Bookings_RoomPeriod'"));
    }

    [Fact]
    public async Task AggregatesRoundTripAndHistorySurvivesConfigurationChanges()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var room = new Room("Room A", 50, 2000m);
        room.SetServices([new("Projector", 500m), new("Wi-Fi", 300m)]);
        var booking = room.Book(At(11).AddTicks(10), At(15), At(10).AddTicks(1), room.Services.Select(x => x.Id).ToArray(), Rules);
        await using (var db = database.Context())
        {
            db.Add(room);
            db.Add(booking);
            db.AddRange(Rules);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = database.Context())
        {
            var loaded = await db.Rooms.Include(x => x.Services).SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(room.Id, loaded.Id);
            Assert.Equal(room.Services.Select(x => x.Id).Order(), loaded.Services.Select(x => x.Id).Order());
            Assert.All(loaded.Services, x => Assert.Equal(room.Id, x.RoomId));
            loaded.SetHourlyRate(3000.001m);
            loaded.SetServices([new("projector", 900.001m), new("Sound", 700m)]);
            db.PricingRules.RemoveRange(await db.PricingRules.ToListAsync(TestContext.Current.CancellationToken));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await database.SqlAsync("UPDATE \"Rooms\" SET \"IsDeleted\"=true");
        await using (var db = database.Context())
        {
            var loaded = await db.Bookings.Include(x => x.Services).Include(x => x.PriceSegments).SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(booking.Id, loaded.Id);
            Assert.Equal(booking.RoomId, loaded.RoomId);
            Assert.Equal(booking.StartsAtUtc, loaded.StartsAtUtc);
            Assert.Equal(booking.EndsAtUtc, loaded.EndsAtUtc);
            Assert.Equal(At(10), loaded.CreatedAtUtc);
            Assert.Equal(DateTimeKind.Utc, loaded.StartsAtUtc.Kind);
            Assert.Equal(9400m, loaded.TotalPrice);
            Assert.Equal(2000m, loaded.HourlyRateSnapshot);
            Assert.Equal(booking.Services.OrderBy(x => x.Id).Select(DescribeService), loaded.Services.OrderBy(x => x.Id).Select(DescribeService));
            Assert.Equal(booking.PriceSegments.OrderBy(x => x.StartsAtUtc).Select(DescribeSegment), loaded.PriceSegments.OrderBy(x => x.StartsAtUtc).Select(DescribeSegment));
            Assert.True((await db.Rooms.SingleAsync(TestContext.Current.CancellationToken)).IsDeleted);
            Assert.Throws<NotSupportedException>(() => ((ICollection<BookingPriceSegment>)loaded.PriceSegments).Clear());
            Assert.Throws<NotSupportedException>(() => ((ICollection<BookedRoomServiceSnapshot>)loaded.Services).Clear());
            var loadedRoom = await db.Rooms.Include(x => x.Services).SingleAsync(TestContext.Current.CancellationToken);
            Assert.Throws<NotSupportedException>(() => ((ICollection<RoomService>)loadedRoom.Services).Clear());
        }

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.SqlAsync("DELETE FROM \"Rooms\""));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
    }

    private static object DescribeService(BookedRoomServiceSnapshot x) => (x.Id, x.BookingId, x.ServiceNameSnapshot, x.ServicePriceSnapshot);
    private static object DescribeSegment(BookingPriceSegment x) => (x.Id, x.BookingId, x.StartsAtUtc, x.EndsAtUtc, x.PricingCodeSnapshot, x.MultiplierSnapshot, x.HourlyRateSnapshot, x.Price);
}
