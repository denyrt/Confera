using Confera.Domain.Bookings;
using static Confera.Domain.Tests.BookingTestData;

namespace Confera.Domain.Tests;

public sealed class BookingPriceCalculatorTests
{
    [Fact]
    public void Calculate_SplitsAtPeakBoundariesWithoutCombiningMultipliers()
    {
        var segments = BookingPriceCalculator.Calculate(At(11), At(15), 2000m, InitialRules());

        Assert.Collection(segments,
            x => AssertSegment(x, At(11), At(12), "standard", 1m, 2000m),
            x => AssertSegment(x, At(12), At(14), "peak", 1.15m, 4600m),
            x => AssertSegment(x, At(14), At(15), "standard", 1m, 2000m));
        Assert.Equal(8600m, segments.Sum(x => x.Price));
        Assert.All(segments, x => Assert.Equal(2000m, x.HourlyRate));
    }

    [Fact]
    public void Calculate_IsIndependentOfRuleOrder()
    {
        var rules = InitialRules();
        var first = BookingPriceCalculator.Calculate(At(6), At(23), 2000m, rules);
        var second = BookingPriceCalculator.Calculate(At(6), At(23), 2000m, rules.Reverse().ToArray());

        Assert.Equal(first.Select(Describe), second.Select(Describe));
        Assert.Equal(new[] { "morning", "standard", "peak", "standard", "evening" },
            first.Select(x => x.PricingCode));
    }

    [Fact]
    public void Calculate_NightRuleIncludesItsPreviousDayOccurrence()
    {
        var segments = BookingPriceCalculator.Calculate(At(1), At(2), 100m,
            [Rule("night", 22, 6, 0.8m)]);

        AssertSegment(Assert.Single(segments), At(1), At(2), "night", 0.8m, 80m);
    }

    [Fact]
    public void Calculate_CrossesMidnightAndSelectsNextDaysTariff()
    {
        var segments = BookingPriceCalculator.Calculate(At(23), At(7).AddDays(1), 100m,
            [Rule("night", 22, 6, 0.8m), Rule("morning", 6, 9, 0.9m)]);

        Assert.Collection(segments,
            x => AssertSegment(x, At(23), At(6).AddDays(1), "night", 0.8m, 560m),
            x => AssertSegment(x, At(6).AddDays(1), At(7).AddDays(1), "morning", 0.9m, 90m));
    }

    [Fact]
    public void Calculate_AcceptsFullyCovered24Hours()
    {
        var segments = BookingPriceCalculator.Calculate(At(10), At(10).AddDays(1), 100m,
            [Rule("day", 9, 18), Rule("night", 18, 9, 0.8m)]);

        Assert.Equal(3, segments.Count);
        Assert.Equal(2100m, segments.Sum(x => x.Price));
        Assert.Equal(At(10), segments[0].StartsAtUtc);
        Assert.Equal(At(10).AddDays(1), segments[^1].EndsAtUtc);
    }

    [Theory]
    [InlineData(4, 14)]
    [InlineData(22, 24)]
    public void Calculate_RejectsUncoveredBeginningOrEnd(int startHour, int endHour)
    {
        var end = endHour == 24 ? At(0).AddDays(1) : At(endHour);

        Assert.Throws<ArgumentException>(() =>
            BookingPriceCalculator.Calculate(At(startHour), end, 100m, InitialRules()));
    }

    [Fact]
    public void Calculate_RejectsGapInMiddle()
    {
        Assert.Throws<ArgumentException>(() => BookingPriceCalculator.Calculate(At(9), At(15), 100m,
            [Rule("first", 9, 11), Rule("second", 12, 15)]));
    }

    [Fact]
    public void Calculate_RejectsEmptyRules()
    {
        Assert.Throws<ArgumentException>(() => BookingPriceCalculator.Calculate(At(9), At(10), 100m, []));
    }

    [Fact]
    public void Calculate_RejectsInvalidRuleSetEvenOutsideBookingPeriod()
    {
        Assert.Throws<ArgumentException>(() => BookingPriceCalculator.Calculate(At(9), At(10), 100m,
            [Rule("day", 9, 18), Rule("first", 20, 23), Rule("second", 21, 22)]));
    }

    [Fact]
    public void Calculate_ChargesFractionalHoursProportionally()
    {
        var segment = Assert.Single(BookingPriceCalculator.Calculate(At(9), At(9, 37, 1), 1200m,
            [Rule("day", 9, 18)]));

        Assert.Equal(740.333m, segment.Price);
    }

    [Fact]
    public void Calculate_RoundsMidpointAwayFromZero()
    {
        var segment = Assert.Single(BookingPriceCalculator.Calculate(At(9), At(9, 30), 0.001m,
            [Rule("day", 9, 18)]));

        Assert.Equal(0.001m, segment.Price);
    }

    [Fact]
    public void Calculate_RoundsEverySegmentBeforeSumming()
    {
        BookingPricingRule[] rules =
        [
            new("first", "First", new TimeOnly(9, 0), new TimeOnly(9, 10), 1m, 0),
            new("second", "Second", new TimeOnly(9, 10), new TimeOnly(9, 20), 1m, 0),
            new("third", "Third", new TimeOnly(9, 20), new TimeOnly(10, 0), 1m, 0)
        ];

        var segments = BookingPriceCalculator.Calculate(At(9), At(9, 30), 0.001m, rules);

        Assert.Equal(3, segments.Count);
        Assert.All(segments, x => Assert.Equal(0m, x.Price));
        Assert.Equal(0m, segments.Sum(x => x.Price));
    }

    [Fact]
    public void Calculate_SplitsAtLowerPriorityRuleBoundaries()
    {
        var segments = BookingPriceCalculator.Calculate(At(10), At(12), 100m,
            [Rule("winner", 9, 18, 1m, 2), Rule("lower", 11, 14, 0.8m, 1)]);

        Assert.Equal(2, segments.Count);
        Assert.All(segments, x => Assert.Equal("winner", x.PricingCode));
        Assert.Equal(At(11), segments[0].EndsAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Calculate_HandlesDateTimeLimits(bool upperLimit)
    {
        var start = upperLimit
            ? new DateTime(9999, 12, 31, 23, 0, 0, DateTimeKind.Utc)
            : new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var segment = Assert.Single(BookingPriceCalculator.Calculate(start, start.AddMinutes(30), 100m,
            [Rule("night", 22, 6)]));

        Assert.Equal(50m, segment.Price);
    }

    [Fact]
    public void Calculate_ReturnsReadOnlySegments()
    {
        var segments = BookingPriceCalculator.Calculate(At(9), At(10), 100m, InitialRules());

        Assert.Throws<NotSupportedException>(() => ((IList<BookingRentalSegment>)segments).Clear());
    }

    private static (DateTime, DateTime, string, decimal, decimal) Describe(BookingRentalSegment x) =>
        (x.StartsAtUtc, x.EndsAtUtc, x.PricingCode, x.Multiplier, x.Price);

    private static void AssertSegment(BookingRentalSegment segment,
        DateTime start, DateTime end, string code, decimal multiplier, decimal price)
    {
        Assert.Equal((start, end, code, multiplier, price), Describe(segment));
        Assert.Equal(DateTimeKind.Utc, segment.StartsAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, segment.EndsAtUtc.Kind);
    }
}
