using System.Data;
using Confera.Application.Rooms;
using Confera.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Confera.Infrastructure.Persistence;

public sealed class RoomStore(
    IDbContextFactory<ConferaDbContext> factory, ILogger<RoomStore> logger) : IRoomStore
{
    public Task<Room?> GetAsync(Guid roomId, CancellationToken cancellationToken) =>
        TranslateErrorsAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            // One statement gives the representation and its version the same database snapshot.
            return await db.Rooms.AsNoTracking().Include(x => x.Services).AsSingleQuery()
                .SingleOrDefaultAsync(x => x.Id == roomId, cancellationToken);
        }, cancellationToken);

    public Task CreateAsync(Room room, CancellationToken cancellationToken) =>
        TranslateErrorsAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            db.Rooms.Add(room);
            // SaveChanges atomically inserts the parent and all current services.
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

    public Task<IRoomTransaction> BeginAsync(CancellationToken cancellationToken) =>
        TranslateErrorsAsync<IRoomTransaction>(async () =>
        {
            var db = await factory.CreateDbContextAsync(cancellationToken);
            try
            {
                var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                return new RoomTransaction(db, transaction, this);
            }
            catch
            {
                await db.DisposeAsync();
                throw;
            }
        }, cancellationToken);

    private sealed class RoomTransaction(
        ConferaDbContext db, IDbContextTransaction transaction, RoomStore store) : IRoomTransaction
    {
        public Task<Room?> GetRoomForUpdateAsync(Guid roomId, CancellationToken cancellationToken) =>
            store.TranslateErrorsAsync(() => RoomQueries.LockAndLoadAsync(db, roomId, cancellationToken), cancellationToken);

        public Task<bool> HasUnfinishedBookingsAsync(Guid roomId, DateTime nowUtc, CancellationToken cancellationToken) =>
            store.TranslateErrorsAsync(() => db.Bookings.AnyAsync(x => x.RoomId == roomId && x.EndsAtUtc > nowUtc,
                cancellationToken), cancellationToken);

        public Task SaveAsync(CancellationToken cancellationToken) =>
            store.TranslateErrorsAsync(() => db.SaveChangesAsync(cancellationToken), cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken) =>
            store.TranslateErrorsAsync(() => transaction.CommitAsync(cancellationToken), cancellationToken);

        public ValueTask DisposeAsync() => new(store.TranslateErrorsAsync(async () =>
        {
            try
            {
                await transaction.DisposeAsync();
            }
            finally
            {
                await db.DisposeAsync();
            }
        }, CancellationToken.None));
    }

    private async Task<T> TranslateErrorsAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && TryGetFailure(error, out var failure))
        {
            if (failure == RoomFailure.PersistenceUnavailable)
            {
                logger.LogError(error, "Room persistence is temporarily unavailable.");
            }

            throw new RoomOperationException(failure, error);
        }
    }

    private Task TranslateErrorsAsync(Func<Task> action, CancellationToken cancellationToken) =>
        TranslateErrorsAsync(async () =>
        {
            await action();
            return true;
        }, cancellationToken);

    private static bool TryGetFailure(Exception error, out RoomFailure failure)
    {
        if (error is DbUpdateConcurrencyException)
        {
            failure = RoomFailure.VersionMismatch;
            return true;
        }

        var cause = PersistenceErrors.Unwrap(error);
        failure = RoomFailure.PersistenceUnavailable;
        if (cause is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UX_Rooms_ActiveName" })
        {
            failure = RoomFailure.NameConflict;
            return true;
        }

        return PersistenceErrors.IsUnavailable(cause);
    }
}
