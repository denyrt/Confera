using Confera.Domain.Bookings;
using static Confera.Domain.Tests.BookingTestData;

namespace Confera.Domain.Tests;

public sealed class BookingPricingRuleTests
{
    [Fact]
    public void ValidateSet_RejectsAdjacentAndDisjointRulesWithSamePriority()
    {
        Assert.Throws<ArgumentException>(() => BookingPricingRule.ValidateSet(
            [Rule("first", 6, 9, 1m, -2), Rule("second", 9, 18, 1m, -2), Rule("third", 20, 23)]));
    }

    [Fact]
    public void ValidateSet_AcceptsInitialTariffs()
    {
        BookingPricingRule.ValidateSet(InitialRules());
    }

    [Theory]
    [InlineData(9, 18, 12, 14)]
    [InlineData(12, 14, 9, 18)]
    [InlineData(9, 12, 11, 14)]
    [InlineData(22, 6, 1, 3)]
    [InlineData(1, 3, 22, 6)]
    [InlineData(22, 6, 23, 5)]
    [InlineData(22, 6, 5, 8)]
    [InlineData(22, 6, 21, 23)]
    public void ValidateSet_RejectsSamePriorityOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd)
    {
        Assert.Throws<ArgumentException>(() => BookingPricingRule.ValidateSet(
            [Rule("first", firstStart, firstEnd, 1m, 0), Rule("second", secondStart, secondEnd, 1m, 0)]));
    }

    [Fact]
    public void ValidateSet_AcceptsAdjacentNightAndDayRules()
    {
        BookingPricingRule.ValidateSet([Rule("night", 22, 6), Rule("day", 6, 22)]);
    }

    [Theory]
    [InlineData(22, 0, 0, 6)]
    [InlineData(0, 6, 22, 0)]
    public void ValidateSet_AcceptsRulesAdjacentAtMidnight(
        int firstStart,
        int firstEnd,
        int secondStart,
        int secondEnd)
    {
        BookingPricingRule.ValidateSet(
            [Rule("first", firstStart, firstEnd), Rule("second", secondStart, secondEnd)]);
    }

    [Fact]
    public void ValidateSet_AcceptsAdjacentRulesAtFinestTimePrecision()
    {
        BookingPricingRule.ValidateSet(
        [
            new("last-microsecond", "Last microsecond", new TimeOnly(TimeOnly.MaxValue.Ticks - 9), TimeOnly.MinValue, 1m, 0),
            new("rest-of-day", "Rest of day", TimeOnly.MinValue, new TimeOnly(TimeOnly.MaxValue.Ticks - 9), 1m, 1)
        ]);
    }

    [Fact]
    public void ValidateSet_RejectsSamePriorityOverlapEvenIfHigherRuleMasksIt()
    {
        Assert.Throws<ArgumentException>(() => BookingPricingRule.ValidateSet(
            [Rule("first", 9, 18, 1m, 0), Rule("second", 12, 14, 1m, 0), Rule("higher", 9, 18, 1m, 2)]));
    }

    [Fact]
    public void ValidateSet_RejectsDuplicateRule()
    {
        var rule = Rule("day", 9, 18);

        Assert.Throws<ArgumentException>(() => BookingPricingRule.ValidateSet([rule, rule]));
    }

    [Fact]
    public void ValidateSet_RejectsNullRule()
    {
        Assert.Throws<ArgumentNullException>(() => BookingPricingRule.ValidateSet([null!]));
    }

    [Fact]
    public void Constructor_RejectsEqualStartAndEnd()
    {
        Assert.Throws<ArgumentException>(() => Rule("invalid", 9, 9));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveMultiplier(int multiplier)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule("invalid", 9, 18, multiplier));
    }
}
