using Confera.Domain.Bookings;

namespace Confera.Application.Bookings;

public sealed record BookingResult(
    Guid BookingId,
    Guid RoomId,
    DateTime Start,
    DateTime End,
    string Currency,
    decimal TotalPrice,
    IReadOnlyList<BookingSegmentResult> Segments,
    IReadOnlyList<BookedServiceResult> Services)
{
    internal static BookingResult FromBooking(Booking booking) => new(
        booking.Id,
        booking.RoomId,
        booking.StartsAtUtc,
        booking.EndsAtUtc,
        "UAH",
        booking.TotalPrice,
        booking.PriceSegments.OrderBy(x => x.StartsAtUtc)
            .Select(x => new BookingSegmentResult(x.StartsAtUtc, x.EndsAtUtc, x.PricingCodeSnapshot,
                x.MultiplierSnapshot, x.HourlyRateSnapshot, x.Price)).ToArray(),
        booking.Services.OrderBy(x => x.ServiceNameSnapshot, StringComparer.Ordinal)
            .Select(x => new BookedServiceResult(x.ServiceNameSnapshot, x.ServicePriceSnapshot)).ToArray());
}

public sealed record BookingSegmentResult(
    DateTime Start,
    DateTime End,
    string PricingCode,
    decimal Multiplier,
    decimal HourlyRate,
    decimal Price);

public sealed record BookedServiceResult(string Name, decimal Price);
