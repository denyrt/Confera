using Confera.Domain.Bookings;

namespace Confera.Domain.Tests;

internal static class BookingTestData
{
    public static DateTime At(int hour, int minute = 0, int second = 0) =>
        new(2026, 9, 15, hour, minute, second, DateTimeKind.Utc);

    public static BookingPricingRule Rule(
        string code, int startHour, int endHour, decimal multiplier = 1m, int priority = 0) =>
        new(code, code, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), multiplier, priority);

    public static BookingPricingRule[] InitialRules() =>
    [
        Rule("standard", 9, 18),
        Rule("morning", 6, 9, 0.9m),
        Rule("evening", 18, 23, 0.8m),
        Rule("peak", 12, 14, 1.15m, 1)
    ];
}
