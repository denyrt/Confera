using System.Diagnostics.CodeAnalysis;

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
        if (!TryCreate(startsAtUtc, endsAtUtc, out var period, out var failure))
        {
            throw failure.ToException();
        }

        return period;
    }

    public static bool TryCreate(DateTime startsAtUtc, DateTime endsAtUtc,
        [NotNullWhen(true)] out RentalPeriod? period,
        [NotNullWhen(false)] out BookingValidationFailure? failure)
    {
        period = null;
        failure = BookingValidation.ValidateRentalPeriod(startsAtUtc, endsAtUtc);
        if (failure is not null)
        {
            return false;
        }

        period = new RentalPeriod(startsAtUtc, endsAtUtc);
        return true;
    }
}
