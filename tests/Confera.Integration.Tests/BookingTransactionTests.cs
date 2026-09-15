using System.Data.Common;
using Confera.Application.Bookings;
using Confera.Domain.Bookings;
using Confera.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Confera.Integration.Tests.BookingTestSupport;

namespace Confera.Integration.Tests;

public sealed class BookingTransactionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task UseCaseCommitsCompleteBookingAndSnapshots()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        var result = await Service(database).CreateAsync(Command(room), TestContext.Current.CancellationToken);

        await using var db = database.Context();
        var saved = await db.Bookings.Include(x => x.PriceSegments).Include(x => x.Services)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(result.BookingId, saved.Id);
        Assert.Equal(room.Id, saved.RoomId);
        Assert.Equal(At(9), saved.CreatedAtUtc);
        Assert.Equal(9400m, saved.TotalPrice);
        Assert.Equal(2000m, saved.HourlyRateSnapshot);
        Assert.Equal(new[] { 2000m, 4600m, 2000m }, saved.PriceSegments.OrderBy(x => x.StartsAtUtc).Select(x => x.Price));
        Assert.Equal(new[] { 300m, 500m }, saved.Services.Select(x => x.ServicePriceSnapshot).Order());
        Assert.All(saved.PriceSegments, x => Assert.Equal(saved.Id, x.BookingId));
        Assert.All(saved.Services, x => Assert.Equal(saved.Id, x.BookingId));
        Assert.Equal(saved.TotalPrice, saved.PriceSegments.Sum(x => x.Price) + saved.Services.Sum(x => x.ServicePriceSnapshot));
    }

    [Theory]
    [InlineData("overlap")]
    [InlineData("adjacent")]
    [InlineData("different_rooms")]
    public async Task ConcurrentUseCasesRespectRoomAndHalfOpenIntervals(string scenario)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, other) = await SeedAsync(database);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var barrier = new RoomLockObserver(2);
        var service = Service(database, barrier);
        var first = Command(room);
        var second = scenario switch
        {
            "adjacent" => first with { StartsAtUtc = first.EndsAtUtc, EndsAtUtc = At(16) },
            "different_rooms" => Command(other),
            _ => first with { StartsAtUtc = At(12), EndsAtUtc = At(16) }
        };

        var results = await Task.WhenAll(Attempt(first), Attempt(second));
        Assert.Equal(2, barrier.ConnectionIds.Distinct().Count());
        if (scenario == "overlap")
        {
            Assert.Single(results, x => x is null);
            Assert.Single(results, x => x == BookingFailure.RoomUnavailable);
        }
        else
        {
            Assert.All(results, Assert.Null);
        }

        Assert.Equal(scenario == "overlap" ? 1L : 2L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Bookings\""));

        async Task<BookingFailure?> Attempt(CreateBookingCommand command)
        {
            try
            {
                await service.CreateAsync(command, timeout.Token);
                return null;
            }
            catch (BookingOperationException error)
            {
                return error.Failure;
            }
        }
    }

    [Theory]
    [InlineData("rate")]
    [InlineData("service_price")]
    [InlineData("service_removed")]
    [InlineData("deleted")]
    [InlineData("clock")]
    public async Task UsesStateAndClockAfterWaitingForRoomLock(string change)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var edit = database.Context();
        await using var editTransaction = await edit.Database.BeginTransactionAsync(timeout.Token);
        await edit.Database.ExecuteSqlInterpolatedAsync($"""SELECT "Id" FROM "Rooms" WHERE "Id" = {room.Id} FOR UPDATE""", timeout.Token);
        var current = await edit.Rooms.Include(x => x.Services).SingleAsync(x => x.Id == room.Id, timeout.Token);
        switch (change)
        {
            case "rate":
                current.SetHourlyRate(3000m);
                break;
            case "service_price":
                current.SetServices([new("Projector", 900m), new("Wi-Fi", 300m)]);
                break;
            case "service_removed":
                current.SetServices([new("Wi-Fi", 300m)]);
                break;
            case "deleted":
                edit.Entry(current).Property(x => x.IsDeleted).CurrentValue = true;
                break;
        }

        await edit.SaveChangesAsync(timeout.Token);
        var observer = new RoomLockObserver();
        var clock = new BookingClock(At(9));
        var service = new CreateBookingService(new BookingStore(new BookingContextFactory(database, observer)), clock);
        var attempt = service.CreateAsync(Command(room), timeout.Token);
        var pid = await observer.Started.Task.WaitAsync(timeout.Token);
        await WaitForBlockedConnectionAsync(database, pid, timeout.Token);
        if (change == "clock")
        {
            clock.Now = At(11).AddTicks(10);
        }

        await editTransaction.CommitAsync(timeout.Token);
        if (change == "deleted")
        {
            var error = await Assert.ThrowsAsync<BookingOperationException>(() => attempt);
            Assert.Equal(BookingFailure.RoomNotFound, error.Failure);
        }
        else if (change is "service_removed" or "clock")
        {
            var error = await Assert.ThrowsAsync<BookingValidationException>(() => attempt);
            Assert.Equal(change == "clock" ? BookingValidationError.InvalidPeriod : BookingValidationError.InvalidServiceSelection, error.Error);
        }
        else
        {
            var result = await attempt;
            Assert.Equal(change == "rate" ? 13700m : 9800m, result.TotalPrice);
        }

        Assert.Equal(change is "rate" or "service_price" ? 1L : 0L,
            await database.ScalarAsync<long>("SELECT count(*) FROM \"Bookings\""));
    }

    [Fact]
    public async Task FailedCommitRollsBackWrittenAggregateAndReleasesRoom()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        var failure = new FailBeforeCommit();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(database, failure).CreateAsync(Command(room), TestContext.Current.CancellationToken));

        Assert.True(failure.ReachedCommit);
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Bookings\""));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"BookingPriceSegments\""));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"BookedRoomServiceSnapshots\""));
        var retryByCaller = await Service(database).CreateAsync(Command(room), TestContext.Current.CancellationToken);
        Assert.Equal(9400m, retryByCaller.TotalPrice);
    }

    [Fact]
    public async Task ConstraintConflictIsTranslatedAndDoesNotLeaveChildren()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        var existing = await Service(database).CreateAsync(Command(room), TestContext.Current.CancellationToken);
        var store = new BookingStore(new BookingContextFactory(database));
        await using (var transaction = await store.BeginAsync(TestContext.Current.CancellationToken))
        {
            var loaded = await transaction.GetRoomForUpdateAsync(room.Id, TestContext.Current.CancellationToken);
            var conflict = loaded!.Book(At(11), At(15), At(9), room.Services.Select(x => x.Id).ToArray(), Rules());
            var error = await Assert.ThrowsAsync<BookingOperationException>(() =>
                transaction.SaveAsync(conflict, TestContext.Current.CancellationToken));
            Assert.Equal(BookingFailure.RoomUnavailable, error.Failure);
        }

        await using var db = database.Context();
        Assert.Equal(existing.BookingId, (await db.Bookings.SingleAsync(TestContext.Current.CancellationToken)).Id);
        Assert.Equal(3L, await database.ScalarAsync<long>("SELECT count(*) FROM \"BookingPriceSegments\""));
        Assert.Equal(2L, await database.ScalarAsync<long>("SELECT count(*) FROM \"BookedRoomServiceSnapshots\""));
    }

    [Fact]
    public async Task CancellationWhileWaitingDoesNotWriteAndReleasesResources()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using var holder = database.Context();
        await using var heldTransaction = await holder.Database.BeginTransactionAsync(timeout.Token);
        await holder.Database.ExecuteSqlInterpolatedAsync($"""SELECT "Id" FROM "Rooms" WHERE "Id" = {room.Id} FOR UPDATE""", timeout.Token);
        var observer = new RoomLockObserver();
        var attempt = Service(database, observer).CreateAsync(Command(room), request.Token);
        var pid = await observer.Started.Task.WaitAsync(timeout.Token);
        await WaitForBlockedConnectionAsync(database, pid, timeout.Token);

        await request.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Bookings\""));
        await heldTransaction.CommitAsync(timeout.Token);
        var next = await Service(database).CreateAsync(Command(room), timeout.Token);
        Assert.Equal(9400m, next.TotalPrice);
    }

    private sealed class FailBeforeCommit : DbTransactionInterceptor
    {
        public bool ReachedCommit { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            ReachedCommit = true;
            throw new InvalidOperationException("Injected failure after SaveChanges and before commit.");
        }
    }
}
