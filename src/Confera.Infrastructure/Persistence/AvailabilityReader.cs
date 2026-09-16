using Confera.Application.Availability;
using Confera.Application.Rooms;
using Confera.Domain.Bookings;
using Microsoft.EntityFrameworkCore;

namespace Confera.Infrastructure.Persistence;

public sealed class AvailabilityReader(IDbContextFactory<ConferaDbContext> factory) : IAvailabilityReader
{
    public Task<IReadOnlyList<BookingPricingRule>> GetPricingRulesAsync(CancellationToken cancellationToken) =>
        TranslateErrorsAsync<IReadOnlyList<BookingPricingRule>>(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            return await db.PricingRules.AsNoTracking().ToListAsync(cancellationToken);
        }, cancellationToken);

    public Task<IReadOnlyList<RoomResult>> GetAvailableRoomsAsync(RentalPeriod period, int capacity,
        int offset, int limit, CancellationToken cancellationToken) =>
        TranslateErrorsAsync<IReadOnlyList<RoomResult>>(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            // Page rooms before expanding services; all returned data shares one statement snapshot.
            return await db.Rooms.AsNoTracking().AsSingleQuery()
                .Where(room => !room.IsDeleted && room.Capacity >= capacity
                    && !db.Bookings.Any(booking => booking.RoomId == room.Id
                        && booking.StartsAtUtc < period.EndsAtUtc && booking.EndsAtUtc > period.StartsAtUtc))
                .OrderBy(room => room.Capacity).ThenBy(room => room.Id)
                .Skip(offset).Take(limit)
                .Select(room => new RoomResult(room.Id, room.Name, room.Capacity, room.HourlyRate, "UAH",
                    room.Services.OrderBy(service => service.Id)
                        .Select(service => new RoomServiceResult(service.Id, service.Name, service.Price)).ToArray()))
                .ToListAsync(cancellationToken);
        }, cancellationToken);

    private static async Task<T> TranslateErrorsAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested
            && PersistenceErrors.IsUnavailable(PersistenceErrors.Unwrap(error)))
        {
            throw new AvailabilityOperationException(AvailabilityFailure.PersistenceUnavailable, error);
        }
    }
}
