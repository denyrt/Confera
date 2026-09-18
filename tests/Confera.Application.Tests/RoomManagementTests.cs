using Confera.Application.Rooms;
using Confera.Domain.Rooms;

namespace Confera.Application.Tests;

public sealed class RoomManagementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LifecycleReadsClockAfterLockAndRejectsUnfinishedBookings(bool delete)
    {
        var store = new TestStore { Unfinished = true };
        var clock = new TestClock(new DateTime(2030, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var afterLock = clock.Now.AddHours(1).AddTicks(9);
        store.AfterLock = () => clock.Now = afterLock;
        var service = new RoomManagementService(store, clock);
        var version = store.Room!.Version;

        var error = delete
            ? (await service.DeleteAsync(store.Room.Id, [version], TestContext.Current.CancellationToken)).Error
            : (await service.UpdateAsync(store.Room.Id, Command(40), [version], TestContext.Current.CancellationToken)).Error;

        Assert.IsType<RoomError.HasUnfinishedBookings>(error);
        Assert.Equal(afterLock.AddTicks(-9), store.CheckedAt);
        Assert.Equal(1, clock.Reads);
        Assert.False(store.Saved);
        Assert.False(store.Committed);
        Assert.Equal(version, store.Room.Version);
        Assert.True(store.Disposed);
    }

    [Fact]
    public async Task CapacityIncreaseAndServiceChangesAreAllowedWithReservations()
    {
        var store = new TestStore { Unfinished = true };
        var version = store.Room!.Version;
        var result = await Service(store).UpdateAsync(store.Room.Id, Command(60), [version], TestContext.Current.CancellationToken);
        Assert.Equal(60, result.Value.Room.Capacity);
        Assert.NotEqual(version, result.Value.Version);
        Assert.Null(store.CheckedAt);
        Assert.True(store.Committed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrStaleVersionCannotWrite(bool missing)
    {
        var store = new TestStore();
        var room = store.Room!;
        var result = await Service(store).UpdateAsync(room.Id, Command(50),
            missing ? null : [Guid.NewGuid()], TestContext.Current.CancellationToken);
        Assert.IsType(missing ? typeof(RoomError.PreconditionRequired) : typeof(RoomError.VersionMismatch), result.Error);
        Assert.False(store.Saved);
        Assert.True(store.Disposed);
    }

    [Fact]
    public async Task PreconditionsUseStateAfterLockAndCaptureCallerInput()
    {
        var store = new TestStore();
        var room = store.Room!;
        Guid[] versions = [room.Version];
        RoomServiceData[] services = [new("Original", 300m)];
        store.AfterLock = () =>
        {
            versions[0] = Guid.NewGuid();
            services[0] = new("Changed externally", 400m);
        };
        var result = await Service(store).UpdateAsync(room.Id, Command(50) with { Services = services }, versions,
            TestContext.Current.CancellationToken);
        Assert.Equal("Original", result.Value.Room.Services.Single().Name);
        Assert.True(store.Committed);

        store.AfterLock = () => room.SetName("Another writer");
        var rejected = await Service(store).UpdateAsync(room.Id,
            Command(50), [room.Version], TestContext.Current.CancellationToken);
        Assert.IsType<RoomError.VersionMismatch>(rejected.Error);
    }

    [Fact]
    public async Task RepeatedDeleteSucceedsWithoutVersionButReadAndUpdateReturnNotFound()
    {
        var store = new TestStore();
        var room = store.Room!;
        room.Delete();
        var deleted = await Service(store).DeleteAsync(room.Id, null, TestContext.Current.CancellationToken);
        Assert.True(deleted.IsSuccess);
        Assert.False(store.Saved);
        Assert.Null(store.CheckedAt);
        var get = await Service(store).GetAsync(room.Id, TestContext.Current.CancellationToken);
        var update = await Service(store).UpdateAsync(room.Id, Command(50),
            [room.Version], TestContext.Current.CancellationToken);
        Assert.IsType<RoomError.NotFound>(get.Error);
        Assert.IsType<RoomError.NotFound>(update.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSaveOrCommitDoesNotReturnSuccessOrReplay(bool commit)
    {
        var store = new TestStore { FailSave = !commit, FailCommit = commit };
        var result = await Service(store).UpdateAsync(store.Room!.Id, Command(60),
            [store.Room.Version], TestContext.Current.CancellationToken);
        Assert.IsType<RoomError.PersistenceUnavailable>(result.Error);
        Assert.False(store.Committed);
        Assert.True(store.Disposed);
        Assert.Equal(1, store.Begins);
    }

    [Fact]
    public async Task InvalidAndCanceledRequestsDoNotOpenPersistence()
    {
        var store = new TestStore();
        var result = await Service(store).CreateAsync(Command(0), TestContext.Current.CancellationToken);
        Assert.IsType<RoomError.InvalidData>(result.Error);
        await Assert.ThrowsAsync<OperationCanceledException>(() => Service(store).UpdateAsync(store.Room!.Id,
            Command(50), null, new CancellationToken(true)));
        Assert.Equal(0, store.Begins);
        Assert.False(store.Saved);
    }

    private static RoomCommand Command(int capacity) => new("Room", capacity, 2000m, [new("Service", 300m)]);

    [Theory]
    [InlineData(false, "begin")]
    [InlineData(true, "begin")]
    [InlineData(false, "dispose_after_commit")]
    [InlineData(true, "dispose_after_commit")]
    [InlineData(false, "dispose_after_rejection")]
    [InlineData(true, "dispose_after_rejection")]
    public async Task AcquisitionAndDisposalFailuresOverrideThePendingOutcome(bool delete, string stage)
    {
        var store = new TestStore
        {
            FailBegin = stage == "begin",
            FailDispose = stage != "begin",
            Unfinished = stage == "dispose_after_rejection"
        };
        var room = store.Room!;

        var error = delete
            ? (await Service(store).DeleteAsync(room.Id, [room.Version], TestContext.Current.CancellationToken)).Error
            : (await Service(store).UpdateAsync(room.Id, Command(40), [room.Version], TestContext.Current.CancellationToken)).Error;

        Assert.IsType<RoomError.PersistenceUnavailable>(error);
        Assert.Equal(1, store.Begins);
        Assert.Equal(stage != "begin", store.Disposed);
        Assert.Equal(stage == "dispose_after_commit", store.Committed);
        Assert.Equal(stage == "dispose_after_commit", store.Saved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringWorkDoesNotBecomeAnExpectedFailure(bool delete)
    {
        var store = new TestStore();
        using var cancellation = new CancellationTokenSource();
        var failure = new RoomOperationException(RoomFailure.PersistenceUnavailable);
        store.AfterLock = () =>
        {
            cancellation.Cancel();
            throw failure;
        };
        var room = store.Room!;

        var error = await Assert.ThrowsAsync<RoomOperationException>(() => delete
            ? Service(store).DeleteAsync(room.Id, [room.Version], cancellation.Token)
            : (Task)Service(store).UpdateAsync(room.Id, Command(50), [room.Version], cancellation.Token));

        Assert.Same(failure, error);
        Assert.True(store.Disposed);
        Assert.False(store.Saved);
    }
    private static RoomManagementService Service(TestStore store) => new(store, TimeProvider.System);

    private sealed class TestClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return new(Now);
        }
    }

    private sealed class TestStore : IRoomStore, IRoomTransaction
    {
        public Room? Room { get; set; } = new("Room", 50, 2000m);
        public Action? AfterLock { get; set; }
        public bool Unfinished { get; set; }
        public DateTime? CheckedAt { get; private set; }
        public bool Saved { get; private set; }
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailSave { get; init; }
        public bool FailCommit { get; init; }
        public bool FailBegin { get; init; }
        public bool FailDispose { get; init; }
        public int Begins { get; private set; }
        public Task<Room?> GetAsync(Guid roomId, CancellationToken cancellationToken) => Task.FromResult(Room);
        public Task CreateAsync(Room room, CancellationToken cancellationToken)
        {
            Saved = true;
            return Task.CompletedTask;
        }
        public Task<IRoomTransaction> BeginAsync(CancellationToken cancellationToken)
        {
            Begins++;
            if (FailBegin) throw new RoomOperationException(RoomFailure.PersistenceUnavailable);
            return Task.FromResult<IRoomTransaction>(this);
        }
        public Task<Room?> GetRoomForUpdateAsync(Guid roomId, CancellationToken cancellationToken)
        {
            AfterLock?.Invoke();
            return Task.FromResult(Room);
        }
        public Task<bool> HasUnfinishedBookingsAsync(Guid roomId, DateTime nowUtc, CancellationToken cancellationToken)
        {
            CheckedAt = nowUtc;
            return Task.FromResult(Unfinished);
        }
        public Task SaveAsync(CancellationToken cancellationToken)
        {
            if (FailSave) throw new RoomOperationException(RoomFailure.PersistenceUnavailable);
            Saved = true;
            return Task.CompletedTask;
        }
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            if (FailCommit) throw new RoomOperationException(RoomFailure.PersistenceUnavailable);
            Committed = true;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (FailDispose) throw new RoomOperationException(RoomFailure.PersistenceUnavailable);
            return ValueTask.CompletedTask;
        }
    }
}
