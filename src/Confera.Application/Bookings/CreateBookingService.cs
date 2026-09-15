using Confera.Domain;
using Confera.Domain.Bookings;

namespace Confera.Application.Bookings;

public sealed record CreateBookingCommand(
    Guid RoomId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    IReadOnlyList<Guid> ServiceIds);

public sealed class CreateBookingService(IBookingStore store, TimeProvider timeProvider)
{
    public async Task<BookingResult> CreateAsync(CreateBookingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.RoomId == Guid.Empty)
        {
            throw new BookingOperationException(BookingFailure.InvalidRequest);
        }

        BookingValidation.RequireRentalPeriod(command.StartsAtUtc, command.EndsAtUtc);
        BookingValidation.RequireServiceIds(command.ServiceIds);
        // Capture caller-owned input before the first asynchronous boundary.
        var serviceIds = command.ServiceIds.ToArray();

        await using var transaction = await store.BeginAsync(cancellationToken);
        var room = await transaction.GetRoomForUpdateAsync(command.RoomId, cancellationToken);
        if (room is null || room.IsDeleted)
        {
            throw new BookingOperationException(BookingFailure.RoomNotFound);
        }

        var rules = await transaction.GetPricingRulesAsync(cancellationToken);
        var nowUtc = UtcPrecision.Floor(timeProvider.GetUtcNow().UtcDateTime);
        BookingValidation.RequireBookingPeriod(command.StartsAtUtc, command.EndsAtUtc, nowUtc);

        if (await transaction.HasOverlapAsync(room.Id, command.StartsAtUtc, command.EndsAtUtc, cancellationToken))
        {
            throw new BookingOperationException(BookingFailure.RoomUnavailable);
        }

        var booking = room.Book(command.StartsAtUtc, command.EndsAtUtc, nowUtc, serviceIds, rules);
        // Prepare the response before writing; success is still returned only after commit.
        var result = BookingResult.FromBooking(booking);
        await transaction.SaveAsync(booking, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
