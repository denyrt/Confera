using System.Data.Common;
using Confera.Application.Bookings;
using Confera.Application.Rooms;
using Confera.Domain.Bookings;
using Confera.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using static Confera.Integration.Tests.BookingTestSupport;

namespace Confera.Integration.Tests;

public sealed class RoomTransactionTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("delete", true)]
    [InlineData("delete", false)]
    [InlineData("capacity", true)]
    [InlineData("capacity", false)]
    [InlineData("rate", true)]
    [InlineData("rate", false)]
    [InlineData("services", true)]
    [InlineData("services", false)]
    public async Task RoomMutationsAndRealBookingsSerializeInBothOrders(string change, bool bookingFirst)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var gate = new PauseBeforeCommit();
        var waiting = new RoomLockObserver();
        var bookings = Service(database, bookingFirst ? gate : waiting);
        var rooms = Rooms(database, bookingFirst ? waiting : gate);
        var edit = new RoomCommand(room.Name, change == "capacity" ? 40 : room.Capacity,
            change == "rate" ? 3000m : room.HourlyRate,
            change == "services" ? [] : room.Services.Select(x => new Confera.Domain.Rooms.RoomServiceData(x.Name, x.Price)).ToArray());

        var first = bookingFirst ? Book() : Edit();
        await gate.Reached.Task.WaitAsync(timeout.Token);
        var second = bookingFirst ? Edit() : Book();
        var pid = await waiting.Started.Task.WaitAsync(timeout.Token);
        await WaitForBlockedConnectionAsync(database, pid, timeout.Token);
        gate.Release.TrySetResult();
        Assert.Null(await first);
        var failure = await second;

        if (bookingFirst && change is "delete" or "capacity")
        {
            Assert.IsType<RoomError.HasUnfinishedBookings>(failure);
        }
        else if (!bookingFirst && change == "delete")
        {
            Assert.IsType<BookingError.RoomNotFound>(failure);
        }
        else if (!bookingFirst && change == "services")
        {
            Assert.IsType<BookingError.InvalidServiceSelection>(failure);
        }
        else
        {
            Assert.Null(failure);
        }

        await using var db = database.Context();
        var savedBookings = await db.Bookings.Include(x => x.Services).ToListAsync(timeout.Token);
        if (!bookingFirst && change is "delete" or "services")
        {
            Assert.Empty(savedBookings);
        }
        else
        {
            var booking = Assert.Single(savedBookings);
            Assert.Equal(!bookingFirst && change == "rate" ? 13700m : 9400m, booking.TotalPrice);
            Assert.Equal(2, booking.Services.Count);
        }

        var savedRoom = await db.Rooms.SingleAsync(x => x.Id == room.Id, timeout.Token);
        Assert.Equal(!bookingFirst && change == "delete", savedRoom.IsDeleted);
        Assert.Equal(!bookingFirst && change == "capacity" ? 40 : 50, savedRoom.Capacity);

        async Task<object?> Book()
        {
            var result = await bookings.CreateAsync(Command(room), timeout.Token);
            return result.IsSuccess ? null : result.Error;
        }

        async Task<object?> Edit()
        {
            if (change == "delete")
            {
                var deletion = await rooms.DeleteAsync(room.Id, [room.Version], timeout.Token);
                return deletion.IsSuccess ? null : deletion.Error;
            }

            var update = await rooms.UpdateAsync(room.Id, edit, [room.Version], timeout.Token);
            return update.IsSuccess ? null : update.Error;
        }
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(12, false)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    public async Task LifecycleDistinguishesFutureOngoingAndCompletedBookings(int nowHour, bool allowed)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        Assert.True((await Service(database).CreateAsync(Command(room), TestContext.Current.CancellationToken)).IsSuccess);
        var rooms = new RoomManagementService(new RoomStore(new BookingContextFactory(database), NullLogger<RoomStore>.Instance), new BookingClock(At(nowHour)));
        if (allowed)
        {
            var updated = await rooms.UpdateAsync(room.Id, Details(room) with { Capacity = 40 }, [room.Version], TestContext.Current.CancellationToken);
            Assert.True((await rooms.DeleteAsync(room.Id, [updated.Value.Version], TestContext.Current.CancellationToken)).IsSuccess);
        }
        else
        {
            var edit = await rooms.UpdateAsync(room.Id,
                Details(room) with { Capacity = 40 }, [room.Version], TestContext.Current.CancellationToken);
            var delete = await rooms.DeleteAsync(room.Id, [room.Version], TestContext.Current.CancellationToken);
            Assert.IsType<RoomError.HasUnfinishedBookings>(edit.Error);
            Assert.IsType<RoomError.HasUnfinishedBookings>(delete.Error);
        }

        await using var db = database.Context();
        Assert.Equal(allowed, (await db.Rooms.SingleAsync(x => x.Id == room.Id, TestContext.Current.CancellationToken)).IsDeleted);
        Assert.Single(await db.Bookings.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BookingThatEndsDuringLockWaitNoLongerBlocksDeletion()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        Assert.True((await Service(database).CreateAsync(Command(room), TestContext.Current.CancellationToken)).IsSuccess);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var holder = database.Context();
        await using var held = await holder.Database.BeginTransactionAsync(timeout.Token);
        await holder.Database.ExecuteSqlInterpolatedAsync($"""SELECT "Id" FROM "Rooms" WHERE "Id" = {room.Id} FOR UPDATE""", timeout.Token);
        var observer = new RoomLockObserver();
        var clock = new BookingClock(At(14));
        var rooms = new RoomManagementService(new RoomStore(new BookingContextFactory(database, observer), NullLogger<RoomStore>.Instance), clock);
        var attempt = rooms.DeleteAsync(room.Id, [room.Version], timeout.Token);
        await WaitForBlockedConnectionAsync(database, await observer.Started.Task.WaitAsync(timeout.Token), timeout.Token);
        clock.Now = At(15).AddTicks(9);
        await held.CommitAsync(timeout.Token);
        Assert.True((await attempt).IsSuccess);
        await using var db = database.Context();
        Assert.True((await db.Rooms.SingleAsync(x => x.Id == room.Id, timeout.Token)).IsDeleted);
    }

    [Fact]
    public async Task CancellationWhileWaitingWritesNothingAndReleasesResources()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using var holder = database.Context();
        await using var held = await holder.Database.BeginTransactionAsync(timeout.Token);
        await holder.Database.ExecuteSqlInterpolatedAsync($"""SELECT "Id" FROM "Rooms" WHERE "Id" = {room.Id} FOR UPDATE""", timeout.Token);
        var observer = new RoomLockObserver();
        var attempt = Rooms(database, observer).UpdateAsync(room.Id, Details(room) with { Name = "Canceled" }, [room.Version], request.Token);
        await WaitForBlockedConnectionAsync(database, await observer.Started.Task.WaitAsync(timeout.Token), timeout.Token);
        await request.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        await held.CommitAsync(timeout.Token);
        var saved = await Rooms(database).GetAsync(room.Id, timeout.Token);
        Assert.Equal(room.Version, saved.Value.Version);
        Assert.Equal(room.Name, saved.Value.Room.Name);
        Assert.True((await Rooms(database).DeleteAsync(room.Id, [room.Version], timeout.Token)).IsSuccess);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAfterSaveRollsBackRoomServicesAndVersion(bool delete)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        var rooms = Rooms(database, new FailBeforeCommit());
        await Assert.ThrowsAsync<InvalidOperationException>(() => delete
            ? rooms.DeleteAsync(room.Id, [room.Version], TestContext.Current.CancellationToken)
            : rooms.UpdateAsync(room.Id, Details(room) with { Name = "Changed", Services = [] }, [room.Version], TestContext.Current.CancellationToken));
        var saved = await Rooms(database).GetAsync(room.Id, TestContext.Current.CancellationToken);
        Assert.Equal(room.Version, saved.Value.Version);
        Assert.Equal(room.Name, saved.Value.Room.Name);
        Assert.Equal(room.Services.Select(x => x.Id).Order(), saved.Value.Room.Services.Select(x => x.Id).Order());
        Assert.True((await Rooms(database).DeleteAsync(room.Id, [room.Version], TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task ConcurrentRenamesOfDifferentRoomsEnforceGlobalActiveNameUniqueness()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, other) = await SeedAsync(database);
        var rooms = Rooms(database, new RoomLockObserver(2));
        var errors = await Task.WhenAll(Rename(room), Rename(other));
        Assert.Single(errors, x => x is null);
        Assert.Single(errors, x => x is RoomError.NameConflict);
        await using var db = database.Context();
        var saved = await db.Rooms.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(saved, x => x.Name == "Shared name");
        Assert.Single(saved, x => x.Version == room.Version || x.Version == other.Version);

        async Task<RoomError?> Rename(Confera.Domain.Rooms.Room value)
        {
            var result = await rooms.UpdateAsync(value.Id, Details(value) with { Name = "Shared name" }, [value.Version], TestContext.Current.CancellationToken);
            return result.IsSuccess ? null : result.Error;
        }
    }

    [Fact]
    public async Task VersionMigrationBackfillsExistingRoomsAndEfRejectsStaleTrackedWrites()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrate: false);
        await using (var db = database.Context())
        {
            await db.GetService<IMigrator>().MigrateAsync("20260914235107_InitialPersistence", TestContext.Current.CancellationToken);
            await database.SqlAsync("INSERT INTO \"Rooms\" (\"Id\",\"Name\",\"Capacity\",\"HourlyRate\") VALUES (gen_random_uuid(),'Legacy A',50,2000),(gen_random_uuid(),'Legacy B',100,3500)");
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
            var rooms = await db.Rooms.ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, rooms.Select(x => x.Version).Distinct().Count());
            Assert.All(rooms, x => Assert.NotEqual(Guid.Empty, x.Version));
        }

        await using var first = database.Context();
        await using var second = database.Context();
        var a = await first.Rooms.SingleAsync(x => x.Name == "Legacy A", TestContext.Current.CancellationToken);
        var b = await second.Rooms.SingleAsync(x => x.Id == a.Id, TestContext.Current.CancellationToken);
        a.SetName("Winner");
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);
        b.SetName("Stale");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private static RoomCommand Details(Confera.Domain.Rooms.Room room) => new(room.Name, room.Capacity, room.HourlyRate,
        room.Services.Select(x => new Confera.Domain.Rooms.RoomServiceData(x.Name, x.Price)).ToArray());
    private static RoomManagementService Rooms(TestDatabase db, params IInterceptor[] interceptors) =>
        new(new RoomStore(new BookingContextFactory(db, interceptors), NullLogger<RoomStore>.Instance), new BookingClock(At(9)));

    private sealed class PauseBeforeCommit : DbTransactionInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class FailBeforeCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected failure after SaveChanges.");
    }
}
