using Confera.Application.Bookings;
using Confera.Domain.Bookings;
using Confera.Domain.Rooms;

namespace Confera.Application.Tests;

public sealed class CreateBookingTests
{
    private static DateTime At(int hour) => new(2030, 1, 15, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreatesPricedSnapshotsAndUsesClockAfterRoomLock()
    {
        var store = new TestStore();
        var clock = new TestClock(At(9));
        store.AfterRoomLock = () => clock.Now = At(10).AddTicks(9);
        var service = new CreateBookingService(store, clock);

        var result = await service.CreateAsync(Command(store), TestContext.Current.CancellationToken);

        Assert.True(store.Committed);
        Assert.True(store.Disposed);
        var booking = Assert.IsType<Booking>(store.Saved);
        Assert.Equal(At(10), booking.CreatedAtUtc);
        Assert.Equal(1, clock.Reads);
        Assert.Equal(booking.Id, result.BookingId);
        Assert.Equal(9400m, result.TotalPrice);
        Assert.Equal("UAH", result.Currency);
        Assert.Equal(new[] { 2000m, 4600m, 2000m }, result.Segments.Select(x => x.Price));
        Assert.Equal(new[] { "Projector", "Wi-Fi" }, result.Services.Select(x => x.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsUnavailableRoomWithoutSaving(bool missing)
    {
        var store = new TestStore { Overlap = !missing };
        var command = Command(store);
        if (missing)
        {
            store.Room = null;
        }

        var error = await Assert.ThrowsAsync<BookingOperationException>(() =>
            Service(store).CreateAsync(command, TestContext.Current.CancellationToken));

        Assert.Equal(missing ? BookingFailure.RoomNotFound : BookingFailure.RoomUnavailable, error.Failure);
        Assert.Null(store.Saved);
        Assert.False(store.Committed);
        Assert.True(store.Disposed);
    }

    [Fact]
    public async Task RejectsStartThatPassedWhileWaitingForRoom()
    {
        var store = new TestStore();
        var clock = new TestClock(At(9));
        store.AfterRoomLock = () => clock.Now = At(11).AddTicks(10);

        var error = await Assert.ThrowsAsync<BookingValidationException>(() =>
            new CreateBookingService(store, clock).CreateAsync(Command(store), TestContext.Current.CancellationToken));

        Assert.Equal(BookingValidationError.InvalidPeriod, error.Error);
        Assert.Null(store.Saved);
        Assert.True(store.Disposed);
    }

    [Theory]
    [InlineData("room")]
    [InlineData("period")]
    [InlineData("utc")]
    [InlineData("precision")]
    [InlineData("duplicate")]
    [InlineData("empty_id")]
    [InlineData("null")]
    public async Task RejectsInvalidInputBeforeOpeningDatabase(string scenario)
    {
        var store = new TestStore();
        var command = Command(store);
        command = scenario switch
        {
            "room" => command with { RoomId = Guid.Empty },
            "period" => command with { EndsAtUtc = command.StartsAtUtc.AddMinutes(29) },
            "utc" => command with { StartsAtUtc = DateTime.SpecifyKind(command.StartsAtUtc, DateTimeKind.Unspecified) },
            "precision" => command with { EndsAtUtc = command.EndsAtUtc.AddTicks(1) },
            "duplicate" => command with { ServiceIds = [command.ServiceIds[0], command.ServiceIds[0]] },
            "empty_id" => command with { ServiceIds = [Guid.Empty] },
            _ => command with { ServiceIds = null! }
        };

        if (scenario == "room")
        {
            var error = await Assert.ThrowsAsync<BookingOperationException>(() => Service(store).CreateAsync(command, TestContext.Current.CancellationToken));
            Assert.Equal(BookingFailure.InvalidRequest, error.Failure);
        }
        else
        {
            await Assert.ThrowsAsync<BookingValidationException>(() => Service(store).CreateAsync(command, TestContext.Current.CancellationToken));
        }

        Assert.Equal(0, store.Begins);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveOrCommitFailureDoesNotReturnSuccessOrRetry(bool failCommit)
    {
        var store = new TestStore { FailSave = !failCommit, FailCommit = failCommit };

        var error = await Assert.ThrowsAsync<BookingOperationException>(() =>
            Service(store).CreateAsync(Command(store), TestContext.Current.CancellationToken));

        Assert.Equal(BookingFailure.PersistenceUnavailable, error.Failure);
        Assert.False(store.Committed);
        Assert.True(store.Disposed);
        Assert.Equal(1, store.Begins);
    }

    [Fact]
    public async Task InvalidTariffConfigurationStaysAnInternalFailure()
    {
        var store = new TestStore();
        store.Rules = [store.Rules[0], store.Rules[0]];

        await Assert.ThrowsAsync<ArgumentException>(() => Service(store).CreateAsync(Command(store), TestContext.Current.CancellationToken));
        Assert.Null(store.Saved);
        Assert.True(store.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsUnknownServiceOrMissingCoverageWithoutWriting(bool missingCoverage)
    {
        var store = new TestStore();
        var command = Command(store);
        if (missingCoverage)
        {
            store.Rules = [];
        }
        else
        {
            command = command with { ServiceIds = [Guid.NewGuid()] };
        }

        var error = await Assert.ThrowsAsync<BookingValidationException>(() => Service(store).CreateAsync(command, TestContext.Current.CancellationToken));
        Assert.Equal(missingCoverage ? BookingValidationError.MissingTariffCoverage : BookingValidationError.InvalidServiceSelection, error.Error);
        Assert.Null(store.Saved);
        Assert.True(store.Disposed);
    }

    [Fact]
    public async Task AlreadyCanceledRequestDoesNotOpenDatabase()
    {
        var store = new TestStore();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Service(store).CreateAsync(Command(store), new CancellationToken(true)));
        Assert.Equal(0, store.Begins);
    }

    private static CreateBookingService Service(TestStore store) => new(store, new TestClock(At(9)));

    private static CreateBookingCommand Command(TestStore store) =>
        new(store.Room!.Id, At(11), At(15), store.Room.Services.Select(x => x.Id).ToArray());

    private sealed class TestClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return new DateTimeOffset(Now);
        }
    }

    private sealed class TestStore : IBookingStore, IBookingTransaction
    {
        public Room? Room { get; set; } = CreateRoom();
        public IReadOnlyList<BookingPricingRule> Rules { get; set; } =
        [
            new("Standard", "Standard", new TimeOnly(9, 0), new TimeOnly(18, 0), 1m, 1),
            new("Peak", "Peak", new TimeOnly(12, 0), new TimeOnly(14, 0), 1.15m, 3)
        ];
        public Action? AfterRoomLock { get; set; }
        public bool Overlap { get; set; }
        public bool FailSave { get; set; }
        public bool FailCommit { get; set; }
        public int Begins { get; private set; }
        public Booking? Saved { get; private set; }
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }

        public Task<IBookingTransaction> BeginAsync(CancellationToken cancellationToken)
        {
            Begins++;
            return Task.FromResult<IBookingTransaction>(this);
        }

        public Task<Room?> GetRoomForUpdateAsync(Guid roomId, CancellationToken cancellationToken)
        {
            AfterRoomLock?.Invoke();
            return Task.FromResult(Room);
        }

        public Task<IReadOnlyList<BookingPricingRule>> GetPricingRulesAsync(CancellationToken cancellationToken) => Task.FromResult(Rules);

        public Task<bool> HasOverlapAsync(Guid roomId, DateTime startsAtUtc, DateTime endsAtUtc, CancellationToken cancellationToken) => Task.FromResult(Overlap);

        public Task SaveAsync(Booking booking, CancellationToken cancellationToken)
        {
            if (FailSave)
            {
                throw new BookingOperationException(BookingFailure.PersistenceUnavailable);
            }

            Saved = booking;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            if (FailCommit)
            {
                throw new BookingOperationException(BookingFailure.PersistenceUnavailable);
            }

            Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        private static Room CreateRoom()
        {
            var room = new Room("Room A", 50, 2000m);
            room.SetServices([new("Projector", 500m), new("Wi-Fi", 300m)]);
            return room;
        }
    }
}
