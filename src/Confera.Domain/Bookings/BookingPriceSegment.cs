using static Confera.Domain.DomainValidation;

namespace Confera.Domain.Bookings;

public sealed class BookingPriceSegment
{
    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public string PricingCodeSnapshot { get; private set;  }
    public decimal MultiplierSnapshot { get; private set; }
    public decimal HourlyRateSnapshot { get; private set; }
    public decimal Price { get; private set; }

    private BookingPriceSegment()
    {
        PricingCodeSnapshot = null!;
    }

    internal BookingPriceSegment(
        Guid bookingId,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        string pricingCodeSnapshot,
        decimal multiplierSnapshot,
        decimal hourlyRateSnapshot,
        decimal price)
    {
        Id = Guid.CreateVersion7();
        BookingId = bookingId;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        PricingCodeSnapshot = RequireText(pricingCodeSnapshot, 64, nameof(pricingCodeSnapshot));
        MultiplierSnapshot = RequirePositive(multiplierSnapshot, nameof(multiplierSnapshot));
        HourlyRateSnapshot = RequirePositive(hourlyRateSnapshot, nameof(hourlyRateSnapshot));
        Price = RequirePositive(price, nameof(price));
    }
}
