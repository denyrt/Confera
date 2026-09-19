using Confera.Application.Common;
using Confera.Domain;
using Confera.Domain.Bookings;

namespace Confera.Application.Availability;

public sealed class SearchAvailabilityService(IAvailabilityReader reader, TimeProvider timeProvider)
{
    public async Task<Result<AvailabilityPage, AvailabilityError>> SearchAsync(
        AvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await SearchCoreAsync(query, cancellationToken);
        }
        catch (AvailabilityOperationException error) when (!cancellationToken.IsCancellationRequested
            && error.Failure == AvailabilityFailure.PersistenceUnavailable)
        {
            return Result<AvailabilityPage, AvailabilityError>.Failure(new AvailabilityError.PersistenceUnavailable());
        }
    }

    private async Task<Result<AvailabilityPage, AvailabilityError>> SearchCoreAsync(
        AvailabilityQuery query, CancellationToken cancellationToken)
    {
        if (query.Capacity <= 0 || query.Page <= 0 || query.PageSize is < 1 or > AvailabilityQuery.MaximumPageSize)
        {
            return Result<AvailabilityPage, AvailabilityError>.Failure(new AvailabilityError.InvalidRequest());
        }

        var offset = ((long)query.Page - 1) * query.PageSize;
        if (offset > int.MaxValue)
        {
            return Result<AvailabilityPage, AvailabilityError>.Failure(new AvailabilityError.InvalidRequest());
        }

        if (!RentalPeriod.TryCreate(query.StartsAtUtc, query.EndsAtUtc, out var period, out var failure))
        {
            return Reject(failure, cancellationToken);
        }

        var nowUtc = UtcPrecision.Floor(timeProvider.GetUtcNow().UtcDateTime);
        if (!BookingValidation.TryValidateBookingPeriod(period, nowUtc, out failure))
        {
            return Reject(failure, cancellationToken);
        }

        var rules = await reader.GetPricingRulesAsync(cancellationToken);
        if (!BookingPriceCalculator.HasFullCoverage(period, rules))
        {
            return Result<AvailabilityPage, AvailabilityError>.Success(new AvailabilityPage([], query.Page, query.PageSize, false));
        }

        var rooms = await reader.GetAvailableRoomsAsync(period, query.Capacity,
            (int)offset, query.PageSize + 1, cancellationToken);
        var page = new AvailabilityPage(rooms.Take(query.PageSize).ToArray(), query.Page, query.PageSize,
            rooms.Count > query.PageSize);
        return Result<AvailabilityPage, AvailabilityError>.Success(page);
    }

    private static Result<AvailabilityPage, AvailabilityError> Reject(BookingValidationFailure failure,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (failure.Kind != BookingValidationError.InvalidPeriod)
        {
            throw new InvalidOperationException("Unexpected availability validation failure.");
        }

        return Result<AvailabilityPage, AvailabilityError>.Failure(new AvailabilityError.InvalidPeriod(failure.Description));
    }
}
