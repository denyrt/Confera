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

        var outcome = await service.CreateAsync(Command(store), TestContext.Current.CancellationToken);
        Assert.True(outcome.IsSuccess);
        var result = outcome.Value;

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

        var result = await Service(store).CreateAsync(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.IsType(missing ? typeof(BookingError.RoomNotFound) : typeof(BookingError.RoomUnavailable), result.Error);
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

        var result = await new CreateBookingService(store, clock).CreateAsync(Command(store), TestContext.Current.CancellationToken);

        var error = Assert.IsType<BookingError.InvalidPeriod>(result.Error);
        Assert.Equal("Booking cannot start in the past.", error.Description);
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

        var result = await Service(store).CreateAsync(command, TestContext.Current.CancellationToken);
        var errorType = scenario switch
        {
            "room" => typeof(BookingError.InvalidRequest),
            "period" or "utc" or "precision" => typeof(BookingError.InvalidPeriod),
            _ => typeof(BookingError.InvalidServiceSelection)
        };
        Assert.IsType(errorType, result.Error);

        Assert.Equal(0, store.Begins);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveOrCommitFailureDoesNotReturnSuccessOrRetry(bool failCommit)
    {
        var store = new TestStore { FailSave = !failCommit, FailCommit = failCommit };

        var result = await Service(store).CreateAsync(Command(store), TestContext.Current.CancellationToken);

        Assert.IsType<BookingError.PersistenceUnavailable>(result.Error);
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

        var result = await Service(store).CreateAsync(command, TestContext.Current.CancellationToken);
        Assert.IsType(missingCoverage ? typeof(BookingError.MissingTariffCoverage) : typeof(BookingError.InvalidServiceSelection), result.Error);
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

    [Theory]
    [InlineData("begin")]
    [InlineData("dispose_after_commit")]
    [InlineData("dispose_after_rejection")]
    public async Task AcquisitionAndDisposalFailuresReturnUnavailableWithoutReplay(string stage)
    {
        var store = new TestStore
        {
            FailBegin = stage == "begin",
            FailDispose = stage != "begin",
            Overlap = stage == "dispose_after_rejection"
        };

        var result = await Service(store).CreateAsync(Command(store), TestContext.Current.CancellationToken);

        Assert.IsType<BookingError.PersistenceUnavailable>(result.Error);
        Assert.Equal(1, store.Begins);
        Assert.Equal(stage != "begin", store.Disposed);
        Assert.Equal(stage == "dispose_after_commit", store.Committed);
        Assert.Equal(stage == "dispose_after_commit", store.Saved is not null);
    }

    [Fact]
    public async Task CancellationDuringWorkDoesNotBecomeAnExpectedFailure()
    {
        var store = new TestStore();
        using var cancellation = new CancellationTokenSource();
        var failure = new BookingOperationException(BookingFailure.PersistenceUnavailable);
        store.AfterRoomLock = () =>
        {
            cancellation.Cancel();
            throw failure;
        };

        var error = await Assert.ThrowsAsync<BookingOperationException>(() =>
            Service(store).CreateAsync(Command(store), cancellation.Token));

        Assert.Same(failure, error);
        Assert.True(store.Disposed);
        Assert.Null(store.Saved);
    }

    private static CreateBookingCommand Command(TestStore store) =>
        new(store.Room!.Id, At(11), At(15), store.Room.Services.Select(x => x.Id).ToArray());

    [Theory]
    [InlineData("room_id", typeof(BookingError.InvalidRequest))]
    [InlineData("period", typeof(BookingError.InvalidPeriod))]
    [InlineData("selection", typeof(BookingError.InvalidServiceSelection))]
    public async Task InvalidInputPrecedenceIsPreservedBeforePersistence(string scenario, Type errorType)
    {
        var store = new TestStore();
        var command = Command(store) with { ServiceIds = [Guid.Empty] };
        if (scenario != "selection") command = command with { EndsAtUtc = command.StartsAtUtc };
        if (scenario == "room_id") command = command with { RoomId = Guid.Empty };

        var result = await Service(store).CreateAsync(command, TestContext.Current.CancellationToken);
        Assert.IsType(errorType, result.Error);
        Assert.Equal(0, store.Begins);
        Assert.Null(store.Saved);
    }

    [Theory]
    [InlineData("missing", typeof(BookingError.RoomNotFound))]
    [InlineData("past", typeof(BookingError.InvalidPeriod))]
    [InlineData("overlap", typeof(BookingError.RoomUnavailable))]
    [InlineData("selection", typeof(BookingError.InvalidServiceSelection))]
    public async Task RejectionsKeepOrderAfterLockAndBeforeTariffConfiguration(string scenario, Type errorType)
    {
        var store = new TestStore { Overlap = scenario != "selection" };
        var command = Command(store) with { ServiceIds = [Guid.NewGuid()] };
        store.Rules = [store.Rules[0], store.Rules[0]];
        if (scenario == "missing") store.Room = null;
        var clock = new TestClock(scenario is "missing" or "past" ? At(12) : At(9));

        var result = await new CreateBookingService(store, clock).CreateAsync(command, TestContext.Current.CancellationToken);
        Assert.IsType(errorType, result.Error);
        Assert.True(store.Disposed);
        Assert.Null(store.Saved);
        Assert.False(store.Committed);
    }

    [Fact]
    public async Task CapturesSelectedIdsBeforeAsynchronousRoomLock()
    {
        var store = new TestStore();
        Guid[] ids = [store.Room!.Services.First().Id];
        var command = Command(store) with { ServiceIds = ids };
        store.AfterRoomLock = () => ids[0] = Guid.NewGuid();

        var result = await Service(store).CreateAsync(command, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Services);
        Assert.True(store.Committed);
    }

    [Theory]
    [InlineData("past")]
    [InlineData("selection")]
    [InlineData("coverage")]
    public async Task CancellationCannotBecomeDomainRejection(string scenario)
    {
        var store = new TestStore { Rules = [] };
        var command = Command(store);
        if (scenario == "selection") command = command with { ServiceIds = [Guid.NewGuid()] };
        using var cancellation = new CancellationTokenSource();
        store.AfterRoomLock = cancellation.Cancel;
        var clock = new TestClock(scenario == "past" ? At(12) : At(9));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new CreateBookingService(store, clock).CreateAsync(command, cancellation.Token));
        Assert.True(store.Disposed);
        Assert.Null(store.Saved);
        Assert.False(store.Committed);
    }

    [Fact]
    public async Task DisposalFailureOverridesDomainRejectionWithoutSaving()
    {
        var store = new TestStore { Rules = [], FailDispose = true };
        var result = await Service(store).CreateAsync(Command(store), TestContext.Current.CancellationToken);
        Assert.IsType<BookingError.PersistenceUnavailable>(result.Error);
        Assert.True(store.Disposed);
        Assert.Null(store.Saved);
        Assert.False(store.Committed);
        Assert.Equal(1, store.Begins);
    }

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
        public bool FailBegin { get; init; }
        public bool FailDispose { get; init; }
        public int Begins { get; private set; }
        public Booking? Saved { get; private set; }
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }

        public Task<IBookingTransaction> BeginAsync(CancellationToken cancellationToken)
        {
            Begins++;
            if (FailBegin) throw new BookingOperationException(BookingFailure.PersistenceUnavailable);
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
            if (FailDispose) throw new BookingOperationException(BookingFailure.PersistenceUnavailable);
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
