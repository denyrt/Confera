using System.Diagnostics.CodeAnalysis;

namespace Confera.Domain.Bookings;

public static class BookingValidation
{
    /// <summary>
    /// Checks date-time interval to be a valid <see cref="RentalPeriod"/>.
    /// </summary>
    /// <param name="startsAtUtc"> Start of interval. </param>
    /// <param name="endsAtUtc"> End of internal. </param>
    /// <exception cref="BookingValidationException"></exception>
    /// <remarks>
    /// UTC kind, whole-microseconds precision, duration between 30 minutes and 24 hours are required to be valid interval.
    /// </remarks>
    public static void RequireRentalPeriod(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        if (ValidateRentalPeriod(startsAtUtc, endsAtUtc) is { } failure)
        {
            throw failure.ToException();
        }
    }

    internal static BookingValidationFailure? ValidateRentalPeriod(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        if (DomainValidation.ValidateUtcInterval(startsAtUtc, endsAtUtc) is not null)
        {
            return new(BookingValidationError.InvalidPeriod,
                "The period must have ordered UTC endpoints with whole-microsecond precision.");
        }

        var duration = endsAtUtc - startsAtUtc;
        if (duration < TimeSpan.FromMinutes(30) || duration > TimeSpan.FromHours(24))
        {
            return new(BookingValidationError.InvalidPeriod,
                "Booking duration must be between 30 minutes and 24 hours.");
        }

        return null;
    }

    public static void RequireBookingPeriod(RentalPeriod period, DateTime nowUtc)
    {
        if (!TryValidateBookingPeriod(period, nowUtc, out var failure))
        {
            throw failure.ToException();
        }
    }

    /// <summary>Checks past start; a null period or malformed clock remains a programming error.</summary>
    public static bool TryValidateBookingPeriod(RentalPeriod period, DateTime nowUtc,
        [NotNullWhen(false)] out BookingValidationFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(period);
        // A bad application clock is an internal error, not invalid client input.
        DomainValidation.RequireUtc(nowUtc, nameof(nowUtc));

        if (period.StartsAtUtc < nowUtc)
        {
            failure = new(BookingValidationError.InvalidPeriod, "Booking cannot start in the past.");
            return false;
        }

        failure = null;
        return true;
    }

    public static void RequireServiceIds(IReadOnlyList<Guid>? serviceIds)
    {
        if (!TryValidateServiceIds(serviceIds, out var failure))
        {
            throw failure.ToException();
        }
    }

    public static bool TryValidateServiceIds([NotNullWhen(true)] IReadOnlyList<Guid>? serviceIds,
        [NotNullWhen(false)] out BookingValidationFailure? failure)
    {
        if (serviceIds is null || serviceIds.Contains(Guid.Empty)
            || serviceIds.Distinct().Count() != serviceIds.Count)
        {
            failure = new(BookingValidationError.InvalidServiceSelection,
                "Supply a list of distinct nonempty service IDs; an empty list is allowed.");
            return false;
        }

        failure = null;
        return true;
    }
}
