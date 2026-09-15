using System.Data.Common;
using System.IO;
using Confera.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Confera.Integration.Tests;

public sealed class SeedTests(PostgresFixture postgres)
{
    private static DemoInitializer Initializer(TestDatabase database, params IInterceptor[] interceptors) =>
        new(new ContextFactory(new DbContextOptionsBuilder<ConferaDbContext>(PersistenceConfiguration.Options(database.ConnectionString, true))
            .AddInterceptors(interceptors).Options), NullLogger<DemoInitializer>.Instance);

    [Fact]
    public async Task ConcurrentSeedIsAtomicOnceAndPreservesSubsequentEditsAndDeletion()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var barrier = new SeedLockBarrier(4);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Initializer(database, barrier).InitializeAsync(TestContext.Current.CancellationToken)));
        Assert.Single(results, x => x == DemoInitializationResult.Completed);
        Assert.Equal(3, results.Count(x => x == DemoInitializationResult.AlreadyCompleted));
        Assert.Equal(3L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Rooms\""));
        Assert.Equal(6L, await database.ScalarAsync<long>("SELECT count(*) FROM \"RoomServices\""));
        Assert.Equal(4L, await database.ScalarAsync<long>("SELECT count(*) FROM \"BookingPricingRules\""));
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM \"InitializationMarkers\""));
        await using (var db = database.Context())
        {
            var room = await db.Rooms.Include(x => x.Services).SingleAsync(x => x.Name == "Room A", TestContext.Current.CancellationToken);
            var rules = await db.PricingRules.ToListAsync(TestContext.Current.CancellationToken);
            var start = new DateTime(2026, 9, 15, 11, 0, 0, DateTimeKind.Utc);
            var booking = room.Book(start, start.AddHours(4), start, room.Services.Select(x => x.Id).ToArray(), rules);
            db.Add(booking);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using (var db = database.Context())
        {
            var booking = await db.Bookings.Include(x => x.PriceSegments).SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(8600m, booking.PriceSegments.Sum(x => x.Price));
            Assert.Equal(9400m, booking.TotalPrice);
        }
        await database.SqlAsync("UPDATE \"Rooms\" SET \"Name\"='Edited', \"IsDeleted\"=true WHERE \"Name\"='Room A'; DELETE FROM \"RoomServices\"; DELETE FROM \"BookingPricingRules\"");
        Assert.Equal(DemoInitializationResult.AlreadyCompleted, await Initializer(database).InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"RoomServices\""));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"BookingPricingRules\""));
        Assert.True(await database.ScalarAsync<bool>("SELECT \"IsDeleted\" FROM \"Rooms\" WHERE \"Name\"='Edited'"));
        await database.SqlAsync("DELETE FROM \"Bookings\"; DELETE FROM \"Rooms\"");
        Assert.Equal(DemoInitializationResult.AlreadyCompleted, await Initializer(database).InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Rooms\""));
    }

    [Fact]
    public async Task NonEmptyUnmarkedDatabaseIsNotChangedOrMarked()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await database.SqlAsync("INSERT INTO \"BookingPricingRules\" VALUES(gen_random_uuid(),'Custom','Custom','22:00','06:00',1,-3)");
        Assert.Equal(DemoInitializationResult.NonEmpty, await Initializer(database).InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Rooms\""));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"InitializationMarkers\""));
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM \"BookingPricingRules\""));
    }

    [Fact]
    public async Task FailureAfterWritingSeedRollsBackRowsAndMarkerAndLaterRunSucceeds()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Initializer(database, new FailAfterSave()).InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Rooms\""));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"RoomServices\""));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"BookingPricingRules\""));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"InitializationMarkers\""));
        Assert.Equal(DemoInitializationResult.Completed, await Initializer(database).InitializeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TransientFailureReplaysWholeTransactionWithFreshContext()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var fault = new TransientAfterSave();
        Assert.Equal(DemoInitializationResult.Completed, await Initializer(database, fault).InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, fault.Contexts.Distinct().Count());
        Assert.Equal(3L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Rooms\""));
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM \"InitializationMarkers\""));
    }

    [Fact]
    public async Task LostCommitAcknowledgementRechecksMarkerWithoutDuplicatingData()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var fault = new LostCommitAcknowledgement();
        Assert.Equal(DemoInitializationResult.AlreadyCompleted, await Initializer(database, fault).InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, fault.Commits);
        Assert.Equal(3L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Rooms\""));
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM \"InitializationMarkers\""));
    }

    private sealed class SeedLockBarrier(int participants) : DbCommandInterceptor
    {
        private int arrived;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref arrived) == participants) ready.SetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected failure after database writes.");
    }

    private sealed class TransientAfterSave : SaveChangesInterceptor
    {
        public List<Guid> Contexts { get; } = [];
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Contexts.Add(eventData.Context!.ContextId.InstanceId);
            if (Contexts.Count == 1) throw new NpgsqlException("Injected transient failure.", new IOException());
            return ValueTask.FromResult(result);
        }
    }

    private sealed class LostCommitAcknowledgement : DbTransactionInterceptor
    {
        public int Commits { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Commits++;
            if (Commits == 1) throw new NpgsqlException("Injected acknowledgement loss.", new IOException());
            return Task.CompletedTask;
        }
    }
}

internal sealed class ContextFactory(DbContextOptions<ConferaDbContext> options) : IDbContextFactory<ConferaDbContext>
{
    public ConferaDbContext CreateDbContext() => new(options);
}
