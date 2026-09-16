using Confera.Application.Rooms;
using Confera.Domain.Bookings;

namespace Confera.Application.Availability;

public interface IAvailabilityReader
{
    Task<IReadOnlyList<BookingPricingRule>> GetPricingRulesAsync(CancellationToken cancellationToken);

    /// <summary>Reads ordered eligible rooms with their services, skipping offset and returning at most limit rooms.</summary>
    Task<IReadOnlyList<RoomResult>> GetAvailableRoomsAsync(RentalPeriod period, int capacity,
        int offset, int limit, CancellationToken cancellationToken);
}
