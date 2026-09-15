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

        var normalized = NameIdentity.Trim(value);

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
        if (value.Kind != DateTimeKind.Utc || value.Ticks % 10 != 0)
        {
            throw new ArgumentException("Date and time must be UTC with whole-microsecond precision.", parameterName);
        }

        return value;
    }

    public static decimal RequireHourlyRate(decimal value, string parameterName) =>
        RequireDecimal(value, 1000m, 100000m, 3, parameterName);

    public static decimal RequireServicePrice(decimal value, string parameterName) =>
        RequireDecimal(value, 200m, 20000m, 3, parameterName);

    public static decimal RequireMultiplier(decimal value, string parameterName) =>
        RequireDecimal(value, 0.50m, 2.00m, 2, parameterName);

    public static decimal RequireSegmentPrice(decimal value, string parameterName) =>
        RequireDecimal(value, 0m, 4800000m, 3, parameterName);

    public static decimal RequireTotal(decimal value, string parameterName) =>
        RequireDecimal(value, 0m, 999999999999999.999m, 3, parameterName);

    public static TimeOnly RequireDailyTime(TimeOnly value, string parameterName)
    {
        if (value.Ticks % 10 != 0)
        {
            throw new ArgumentException("Daily time must have whole-microsecond precision.", parameterName);
        }

        return value;
    }

    private static decimal RequireDecimal(decimal value, decimal minimum, decimal maximum, int scale, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Value must be between {minimum} and {maximum}.");
        }

        if (decimal.Round(value, scale) != value)
        {
            throw new ArgumentException($"Value must have at most {scale} fractional digits.", parameterName);
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
