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
        RequireUtc(startsAtUtc, nameof(startsAtUtc));
        RequireUtc(endsAtUtc, nameof(endsAtUtc));
        RequireUtc(nowUtc, nameof(nowUtc));

        if (endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentException("Booking must end after it starts.", nameof(endsAtUtc));
        }

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
}
