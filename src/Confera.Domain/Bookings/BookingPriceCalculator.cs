using Confera.Domain.Rooms;

namespace Confera.Domain.Bookings;

public static class BookingPriceCalculator
{
    public static void Evaluate(
        DateTime startTime,
        DateTime endTime,
        Room room,
        IReadOnlyList<BookingPriceSegment> priceSegments)
    {
        throw new NotImplementedException();
    }
}
