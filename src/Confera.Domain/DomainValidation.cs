using System.Numerics;

namespace Confera.Domain;

internal static class DomainValidation
{
    public static Guid RequireGuid(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Not empty guid is required.", parameterName);
        }

        return id;
    }

    public static string RequireText(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }

    public static T RequirePositive<T>(T value, string parameterName) where T : INumber<T>
    {
        if (value <= T.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be greater than 0.");
        }

        return value;
    }

    public static void RequireBookingPeriod(
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        DateTime nowUtc)
    {
        RequireRentalPeriod(startsAtUtc, endsAtUtc);
        RequireUtc(nowUtc, nameof(nowUtc));

        if (startsAtUtc < nowUtc)
        {
            throw new ArgumentException("Booking cannot start in the past.", nameof(startsAtUtc));
        }
    }

    public static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Date and time must be in UTC.", parameterName);
        }

        return value;
    }

    public static decimal RequireMoney(decimal value, string parameterName)
    {
        RequirePositive(value, parameterName);

        if (decimal.Round(value, 3) != value)
        {
            throw new ArgumentException("Money must have at most three fractional digits.", parameterName);
        }

        return value;
    }

    public static decimal RequireNonNegative(decimal value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value cannot be negative.");
        }

        return value;
    }

    public static void RequireUtcInterval(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        RequireUtc(startsAtUtc, nameof(startsAtUtc));
        RequireUtc(endsAtUtc, nameof(endsAtUtc));

        if (endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentException("Interval must end after it starts.", nameof(endsAtUtc));
        }
    }

    public static void RequireRentalPeriod(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        RequireUtcInterval(startsAtUtc, endsAtUtc);

        var duration = endsAtUtc - startsAtUtc;

        if (duration < TimeSpan.FromMinutes(30) || duration > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(endsAtUtc), "Booking duration must be between 30 minutes and 24 hours.");
        }
    }
}
