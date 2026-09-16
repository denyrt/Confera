namespace Confera.Domain.Bookings;

public sealed record RentalPeriod
{
    public DateTime StartsAtUtc { get; }
    public DateTime EndsAtUtc { get; }

    public TimeSpan Duration => EndsAtUtc - StartsAtUtc;

    private RentalPeriod(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
    }

    public static RentalPeriod Create(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        BookingValidation.RequireRentalPeriod(startsAtUtc, endsAtUtc);
        return new RentalPeriod(startsAtUtc, endsAtUtc);
    }
}
