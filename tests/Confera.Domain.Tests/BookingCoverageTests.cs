using Confera.Domain.Bookings;
using static Confera.Domain.Tests.BookingTestData;

namespace Confera.Domain.Tests;

public sealed class BookingCoverageTests
{
    [Theory]
    [InlineData("full", true)]
    [InlineData("beginning", false)]
    [InlineData("middle", false)]
    [InlineData("end", false)]
    [InlineData("empty", false)]
    [InlineData("overnight", true)]
    [InlineData("previous_day", true)]
    [InlineData("24_hours", true)]
    public void CoverageAgreesWithPricing(string scenario, bool covered)
    {
        var period = Period(At(11), At(15));
        var rules = InitialRules();
        switch (scenario)
        {
            case "beginning": period = Period(At(4), At(14)); break;
            case "middle": rules = [Rule("first", 9, 12), Rule("second", 13, 18)]; break;
            case "end": period = Period(At(22), At(0).AddDays(1)); break;
            case "empty": rules = []; break;
            case "overnight":
                period = Period(At(23), At(7).AddDays(1));
                rules = [Rule("night", 22, 6), Rule("morning", 6, 9)];
                break;
            case "previous_day":
                period = Period(At(1), At(2));
                rules = [Rule("night", 22, 6)];
                break;
            case "24_hours":
                period = Period(At(10), At(10).AddDays(1));
                rules = [Rule("day", 9, 18), Rule("night", 18, 9)];
                break;
        }

        Assert.Equal(covered, BookingPriceCalculator.HasFullCoverage(period, rules));
        Assert.Equal(covered, BookingPriceCalculator.TryCalculate(period, 2000m, rules, out var segments, out var failure));
        if (covered)
        {
            Assert.NotNull(segments);
            Assert.Null(failure);
            Assert.Equal(period.StartsAtUtc, segments[0].StartsAtUtc);
            Assert.Equal(period.EndsAtUtc, segments[^1].EndsAtUtc);
        }
        else
        {
            Assert.Null(segments);
            Assert.NotNull(failure);
            Assert.Equal(BookingValidationError.MissingTariffCoverage, failure.Kind);
            Assert.Equal("The entire booking period must be covered by pricing rules.", failure.Description);
            var error = Assert.Throws<BookingValidationException>(() => BookingPriceCalculator.Calculate(period, 2000m, rules));
            Assert.Equal(BookingValidationError.MissingTariffCoverage, error.Error);
            Assert.Equal(failure.Description, error.Message);
        }
    }

    [Fact]
    public void InvalidConfigurationOutsideRequestedPeriodIsNotMissingCoverage()
    {
        var period = Period(At(10), At(11));
        BookingPricingRule[] rules = [Rule("day", 9, 18), Rule("first", 20, 22, 1m, 2), Rule("second", 22, 23, 1m, 2)];
        Assert.Throws<ArgumentException>(() => BookingPriceCalculator.HasFullCoverage(period, rules));
        Assert.Throws<ArgumentException>(() => BookingPriceCalculator.Calculate(period, 2000m, rules));
        Assert.Throws<ArgumentException>(() => BookingPriceCalculator.TryCalculate(period, 2000m, rules, out _, out _));
    }

    [Fact]
    public void RentalPeriodConsumersRejectNullExplicitly()
    {
        Assert.Throws<ArgumentNullException>(() => BookingPriceCalculator.HasFullCoverage(null!, InitialRules()));
        Assert.Throws<ArgumentNullException>(() => BookingPriceCalculator.Calculate(null!, 2000m, InitialRules()));
        Assert.Throws<ArgumentNullException>(() => BookingValidation.RequireBookingPeriod(null!, At(9)));
        Assert.Throws<ArgumentNullException>(() => BookingPriceCalculator.TryCalculate(null!, 2000m, InitialRules(), out _, out _));
        Assert.Throws<ArgumentNullException>(() => BookingValidation.TryValidateBookingPeriod(null!, At(9), out _));
    }
}
