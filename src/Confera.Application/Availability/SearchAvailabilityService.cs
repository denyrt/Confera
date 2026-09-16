using Confera.Domain;
using Confera.Domain.Bookings;

namespace Confera.Application.Availability;

public sealed class SearchAvailabilityService(IAvailabilityReader reader, TimeProvider timeProvider)
{
    public async Task<AvailabilityPage> SearchAsync(AvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Capacity <= 0 || query.Page <= 0 || query.PageSize is < 1 or > AvailabilityQuery.MaximumPageSize)
        {
            throw new AvailabilityOperationException(AvailabilityFailure.InvalidRequest);
        }

        var offset = ((long)query.Page - 1) * query.PageSize;
        if (offset > int.MaxValue)
        {
            throw new AvailabilityOperationException(AvailabilityFailure.InvalidRequest);
        }

        var period = RentalPeriod.Create(query.StartsAtUtc, query.EndsAtUtc);
        var nowUtc = UtcPrecision.Floor(timeProvider.GetUtcNow().UtcDateTime);
        BookingValidation.RequireBookingPeriod(period, nowUtc);

        var rules = await reader.GetPricingRulesAsync(cancellationToken);
        if (!BookingPriceCalculator.HasFullCoverage(period, rules))
        {
            return new AvailabilityPage([], query.Page, query.PageSize, false);
        }

        var rooms = await reader.GetAvailableRoomsAsync(period, query.Capacity,
            (int)offset, query.PageSize + 1, cancellationToken);
        return new AvailabilityPage(rooms.Take(query.PageSize).ToArray(), query.Page, query.PageSize,
            rooms.Count > query.PageSize);
    }
}
