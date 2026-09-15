namespace Confera.Domain.Bookings;

/// <summary>A calculated rental segment before it is attached to a booking.</summary>
public sealed class BookingRentalSegment
{
    public DateTime StartsAtUtc { get; }
    public DateTime EndsAtUtc { get; }
    public string PricingCode { get; }
    public decimal Multiplier { get; }
    public decimal HourlyRate { get; }
    public decimal Price { get; }

    internal BookingRentalSegment(
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        string pricingCode,
        decimal multiplier,
        decimal hourlyRate)
    {
        DomainValidation.RequireUtcInterval(startsAtUtc, endsAtUtc);

        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        PricingCode = DomainValidation.RequireText(pricingCode, 64, nameof(pricingCode));
        Multiplier = DomainValidation.RequireMultiplier(multiplier, nameof(multiplier));
        HourlyRate = DomainValidation.RequireHourlyRate(hourlyRate, nameof(hourlyRate));

        var hours = (endsAtUtc - startsAtUtc).Ticks / (decimal)TimeSpan.TicksPerHour;
        var unroundedPrice = hours * HourlyRate * Multiplier;
        var roundedPrice = decimal.Round(unroundedPrice, 3, MidpointRounding.AwayFromZero);

        Price = DomainValidation.RequireSegmentPrice(roundedPrice, nameof(Price));
    }
}
