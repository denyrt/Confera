using Confera.Application.Common;
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
    public async Task<Result<BookingResult, BookingError>> CreateAsync(
        CreateBookingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await CreateCoreAsync(command, cancellationToken);
        }
        catch (BookingValidationException error) when (!cancellationToken.IsCancellationRequested)
        {
            BookingError failure = error.Error switch
            {
                BookingValidationError.InvalidPeriod => new BookingError.InvalidPeriod(error.Message),
                BookingValidationError.InvalidServiceSelection => new BookingError.InvalidServiceSelection(error.Message),
                BookingValidationError.MissingTariffCoverage => new BookingError.MissingTariffCoverage(error.Message),
                _ => throw new InvalidOperationException("Unknown booking validation failure.", error)
            };
            return Result<BookingResult, BookingError>.Failure(failure);
        }
        catch (BookingOperationException error) when (!cancellationToken.IsCancellationRequested
            && error.Failure == BookingFailure.RoomUnavailable)
        {
            return Result<BookingResult, BookingError>.Failure(new BookingError.RoomUnavailable());
        }
        catch (BookingOperationException error) when (!cancellationToken.IsCancellationRequested
            && error.Failure == BookingFailure.PersistenceUnavailable)
        {
            return Result<BookingResult, BookingError>.Failure(new BookingError.PersistenceUnavailable());
        }
    }

    private async Task<Result<BookingResult, BookingError>> CreateCoreAsync(
        CreateBookingCommand command, CancellationToken cancellationToken)
    {
        if (command.RoomId == Guid.Empty)
        {
            return Result<BookingResult, BookingError>.Failure(new BookingError.InvalidRequest());
        }

        var rentalPeriod = RentalPeriod.Create(command.StartsAtUtc, command.EndsAtUtc);

        BookingValidation.RequireServiceIds(command.ServiceIds);
        // Capture caller-owned input before the first asynchronous boundary.
        var serviceIds = command.ServiceIds.ToArray();

        await using var transaction = await store.BeginAsync(cancellationToken);
        var room = await transaction.GetRoomForUpdateAsync(command.RoomId, cancellationToken);
        if (room is null || room.IsDeleted)
        {
            return Result<BookingResult, BookingError>.Failure(new BookingError.RoomNotFound());
        }

        var rules = await transaction.GetPricingRulesAsync(cancellationToken);
        var nowUtc = UtcPrecision.Floor(timeProvider.GetUtcNow().UtcDateTime);

        BookingValidation.RequireBookingPeriod(rentalPeriod, nowUtc);

        if (await transaction.HasOverlapAsync(room.Id, command.StartsAtUtc, command.EndsAtUtc, cancellationToken))
        {
            return Result<BookingResult, BookingError>.Failure(new BookingError.RoomUnavailable());
        }

        var booking = room.Book(rentalPeriod, nowUtc, serviceIds, rules);
        // Prepare the response before writing; success is still returned only after commit.
        var result = BookingResult.FromBooking(booking);
        await transaction.SaveAsync(booking, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<BookingResult, BookingError>.Success(result);
    }
}
