namespace Confera.Domain.Bookings;

public sealed class BookingPriceSegment
{
    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public string PricingCodeSnapshot { get; private set; }
    public decimal MultiplierSnapshot { get; private set; }
    public decimal HourlyRateSnapshot { get; private set; }
    public decimal Price { get; private set; }

    private BookingPriceSegment()
    {
        PricingCodeSnapshot = null!;
    }

    internal BookingPriceSegment(Guid bookingId, BookingRentalSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        Id = Guid.CreateVersion7();
        BookingId = DomainValidation.RequireGuid(bookingId, nameof(bookingId));
        StartsAtUtc = segment.StartsAtUtc;
        EndsAtUtc = segment.EndsAtUtc;
        PricingCodeSnapshot = segment.PricingCode;
        MultiplierSnapshot = segment.Multiplier;
        HourlyRateSnapshot = segment.HourlyRate;
        Price = DomainValidation.RequireSegmentPrice(segment.Price, nameof(segment.Price));
    }
}
