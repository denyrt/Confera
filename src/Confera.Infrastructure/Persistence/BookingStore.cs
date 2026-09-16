using System.Data;
using Confera.Application.Bookings;
using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Confera.Infrastructure.Persistence;

public sealed class BookingStore(IDbContextFactory<ConferaDbContext> factory) : IBookingStore
{
    public Task<IBookingTransaction> BeginAsync(CancellationToken cancellationToken) =>
        TranslateErrorsAsync<IBookingTransaction>(async () =>
        {
            var db = await factory.CreateDbContextAsync(cancellationToken);
            try
            {
                var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                return new BookingTransaction(db, transaction);
            }
            catch
            {
                await db.DisposeAsync();
                throw;
            }
        }, cancellationToken);

    private sealed class BookingTransaction(ConferaDbContext db, IDbContextTransaction transaction) : IBookingTransaction
    {
        public Task<Room?> GetRoomForUpdateAsync(Guid roomId, CancellationToken cancellationToken) =>
            TranslateErrorsAsync(() => RoomQueries.LockAndLoadAsync(db, roomId, cancellationToken), cancellationToken);

        public Task<IReadOnlyList<BookingPricingRule>> GetPricingRulesAsync(CancellationToken cancellationToken) =>
            TranslateErrorsAsync<IReadOnlyList<BookingPricingRule>>(async () =>
                await db.PricingRules.AsNoTracking().ToListAsync(cancellationToken), cancellationToken);

        public Task<bool> HasOverlapAsync(Guid roomId, DateTime startsAtUtc, DateTime endsAtUtc, CancellationToken cancellationToken) =>
            TranslateErrorsAsync(() => db.Bookings.AnyAsync(
                x => x.RoomId == roomId && x.StartsAtUtc < endsAtUtc && x.EndsAtUtc > startsAtUtc,
                cancellationToken), cancellationToken);

        public Task SaveAsync(Booking booking, CancellationToken cancellationToken) =>
            TranslateErrorsAsync(async () =>
            {
                db.Bookings.Add(booking);
                await db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken) =>
            TranslateErrorsAsync(() => transaction.CommitAsync(cancellationToken), cancellationToken);

        public ValueTask DisposeAsync() => new(TranslateErrorsAsync(async () =>
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

    // Error translation is the only shared wrapper: no retries or transaction replay.
    private static async Task<T> TranslateErrorsAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && TryGetFailure(error, out var failure))
        {
            throw new BookingOperationException(failure, error);
        }
    }

    private static Task TranslateErrorsAsync(Func<Task> action, CancellationToken cancellationToken) =>
        TranslateErrorsAsync(async () =>
        {
            await action();
            return true;
        }, cancellationToken);

    private static bool TryGetFailure(Exception error, out BookingFailure failure)
    {
        var cause = PersistenceErrors.Unwrap(error);

        failure = BookingFailure.PersistenceUnavailable;

        if (cause is PostgresException
            {
                SqlState: PostgresErrorCodes.ExclusionViolation,
                ConstraintName: "EX_Bookings_RoomPeriod"
            })
        {
            failure = BookingFailure.RoomUnavailable;
            return true;
        }

        return PersistenceErrors.IsUnavailable(cause);
    }
}
