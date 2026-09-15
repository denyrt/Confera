namespace Confera.Domain.Bookings;

public static class BookingValidation
{
    public static void RequireRentalPeriod(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        try
        {
            DomainValidation.RequireUtcInterval(startsAtUtc, endsAtUtc);
        }
        catch (ArgumentException)
        {
            throw new BookingValidationException(BookingValidationError.InvalidPeriod,
                "The period must have ordered UTC endpoints with whole-microsecond precision.");
        }

        var duration = endsAtUtc - startsAtUtc;
        if (duration < TimeSpan.FromMinutes(30) || duration > TimeSpan.FromHours(24))
        {
            throw new BookingValidationException(BookingValidationError.InvalidPeriod,
                "Booking duration must be between 30 minutes and 24 hours.");
        }
    }

    public static void RequireBookingPeriod(DateTime startsAtUtc, DateTime endsAtUtc, DateTime nowUtc)
    {
        RequireRentalPeriod(startsAtUtc, endsAtUtc);
        // A bad application clock is an internal error, not invalid client input.
        DomainValidation.RequireUtc(nowUtc, nameof(nowUtc));

        if (startsAtUtc < nowUtc)
        {
            throw new BookingValidationException(BookingValidationError.InvalidPeriod,
                "Booking cannot start in the past.");
        }
    }

    public static void RequireServiceIds(IReadOnlyList<Guid>? serviceIds)
    {
        if (serviceIds is null || serviceIds.Contains(Guid.Empty)
            || serviceIds.Distinct().Count() != serviceIds.Count)
        {
            throw new BookingValidationException(BookingValidationError.InvalidServiceSelection,
                "Supply a list of distinct nonempty service IDs; an empty list is allowed.");
        }
    }
}
