using System.Diagnostics.CodeAnalysis;
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
        if (!TryNormalizeText(value, maxLength, parameterName, out var normalized, out var failure))
        {
            throw failure.ToException();
        }

        return normalized;
    }

    public static bool TryNormalizeText(string? value, int maxLength, string parameterName,
        [NotNullWhen(true)] out string? normalized, [NotNullWhen(false)] out InputFailure? failure)
    {
        normalized = null;
        failure = null;
        if (value is null)
        {
            failure = InputFailure.Null(parameterName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            failure = new(InputFailureKind.Invalid,
                "The value cannot be an empty string or composed entirely of whitespace.", parameterName);
            return false;
        }

        var trimmed = NameIdentity.Trim(value);
        if (trimmed.Length > maxLength)
        {
            failure = new(InputFailureKind.Invalid, $"Value cannot exceed {maxLength} characters.", parameterName);
            return false;
        }

        normalized = trimmed;
        return true;
    }

    public static T RequirePositive<T>(T value, string parameterName) where T : INumber<T>
    {
        if (ValidatePositive(value, parameterName) is { } failure)
        {
            throw failure.ToException();
        }

        return value;
    }

    public static InputFailure? ValidatePositive<T>(T value, string parameterName) where T : INumber<T> =>
        value <= T.Zero
            ? new(InputFailureKind.OutOfRange, "Value must be greater than 0.", parameterName)
            : null;

    public static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (ValidateUtc(value, parameterName) is { } failure)
        {
            throw failure.ToException();
        }

        return value;
    }

    private static InputFailure? ValidateUtc(DateTime value, string parameterName) =>
        value.Kind != DateTimeKind.Utc || value.Ticks % 10 != 0
            ? new(InputFailureKind.Invalid, "Date and time must be UTC with whole-microsecond precision.", parameterName)
            : null;

    public static decimal RequireHourlyRate(decimal value, string parameterName)
    {
        if (ValidateHourlyRate(value, parameterName) is { } failure)
        {
            throw failure.ToException();
        }

        return value;
    }

    public static InputFailure? ValidateHourlyRate(decimal value, string parameterName) =>
        ValidateDecimal(value, 1000m, 100000m, 3, parameterName);

    public static decimal RequireServicePrice(decimal value, string parameterName)
    {
        if (ValidateServicePrice(value, parameterName) is { } failure)
        {
            throw failure.ToException();
        }

        return value;
    }

    public static InputFailure? ValidateServicePrice(decimal value, string parameterName) =>
        ValidateDecimal(value, 200m, 20000m, 3, parameterName);

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
        if (ValidateDecimal(value, minimum, maximum, scale, parameterName) is { } failure)
        {
            throw failure.ToException();
        }

        return value;
    }

    private static InputFailure? ValidateDecimal(decimal value, decimal minimum, decimal maximum, int scale, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            return new(InputFailureKind.OutOfRange, $"Value must be between {minimum} and {maximum}.", parameterName);
        }

        if (decimal.Round(value, scale) != value)
        {
            return new(InputFailureKind.Invalid, $"Value must have at most {scale} fractional digits.", parameterName);
        }

        return null;
    }

    public static void RequireUtcInterval(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        if (ValidateUtcInterval(startsAtUtc, endsAtUtc) is { } failure)
        {
            throw failure.ToException();
        }
    }

    public static InputFailure? ValidateUtcInterval(DateTime startsAtUtc, DateTime endsAtUtc) =>
        ValidateUtc(startsAtUtc, nameof(startsAtUtc))
        ?? ValidateUtc(endsAtUtc, nameof(endsAtUtc))
        ?? (endsAtUtc <= startsAtUtc
            ? new(InputFailureKind.Invalid, "Interval must end after it starts.", nameof(endsAtUtc))
            : null);
}
