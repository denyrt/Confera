namespace Confera.Domain.Bookings;

public static class BookingPriceCalculator
{
    /// <summary>Checks full tariff coverage without calculating a rental price.</summary>
    /// <remarks>Invalid periods and rule configurations throw; missing coverage returns false.</remarks>
    public static bool HasFullCoverage(RentalPeriod period, IReadOnlyList<BookingPricingRule> rules)
    {
        BookingPricingRule.ValidateSet(rules);

        return GetCoveredSegments(period.StartsAtUtc, period.EndsAtUtc, rules) is not null;
    }

    public static IReadOnlyList<BookingRentalSegment> Calculate(
        RentalPeriod period,
        decimal hourlyRate,
        IReadOnlyList<BookingPricingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(period, nameof(period));
        DomainValidation.RequireHourlyRate(hourlyRate, nameof(hourlyRate));
        BookingPricingRule.ValidateSet(rules);

        var coveredSegments = GetCoveredSegments(period.StartsAtUtc, period.EndsAtUtc, rules)
            ?? throw new BookingValidationException(BookingValidationError.MissingTariffCoverage,
                "The entire booking period must be covered by pricing rules.");
        var segments = new List<BookingRentalSegment>();

        foreach (var segment in coveredSegments)
        {
            segments.Add(new BookingRentalSegment(
                new DateTime(segment.StartsAtTicks, DateTimeKind.Utc),
                new DateTime(segment.EndsAtTicks, DateTimeKind.Utc),
                segment.Rule.Code,
                segment.Rule.Multiplier,
                hourlyRate));
        }

        return segments.AsReadOnly();
    }

    private static List<RuleInterval>? GetCoveredSegments(
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        IReadOnlyList<BookingPricingRule> rules)
    {
        var intervals = ExpandRules(startsAtUtc, endsAtUtc, rules);
        var boundaries = GetSegmentBoundaries(startsAtUtc, endsAtUtc, intervals);
        var segments = new List<RuleInterval>();

        // Preserve every rule boundary: merging segments could change price rounding.
        for (var i = 0; i < boundaries.Length - 1; i++)
        {
            var segmentStartTicks = boundaries[i];
            var segmentEndTicks = boundaries[i + 1];
            var rule = FindCoveringRule(intervals, segmentStartTicks, segmentEndTicks);
            if (rule is null)
            {
                return null;
            }

            segments.Add(new RuleInterval(segmentStartTicks, segmentEndTicks, rule));
        }

        return segments;
    }

    private static long[] GetSegmentBoundaries(
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        IReadOnlyList<RuleInterval> intervals)
    {
        var boundaries = new SortedSet<long> { startsAtUtc.Ticks, endsAtUtc.Ticks };

        foreach (var interval in intervals)
        {
            boundaries.Add(interval.StartsAtTicks);
            boundaries.Add(interval.EndsAtTicks);
        }

        return boundaries.ToArray();
    }

    private static BookingPricingRule? FindCoveringRule(
        IReadOnlyList<RuleInterval> intervals,
        long segmentStartTicks,
        long segmentEndTicks)
    {
        return intervals
            .Where(interval => interval.StartsAtTicks <= segmentStartTicks
                && interval.EndsAtTicks >= segmentEndTicks)
            .Select(interval => interval.Rule)
            .MaxBy(rule => rule.Priority);
    }

    private static List<RuleInterval> ExpandRules(
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        IReadOnlyList<BookingPricingRule> rules)
    {
        var intervals = new List<RuleInterval>();

        // Clip neighboring daily occurrences using ticks so that DateTime's
        // limits do not require constructing an out-of-range DateTime.
        var previousDayTicks = startsAtUtc.Date.Ticks - TimeSpan.TicksPerDay;
        var lastDayTicks = endsAtUtc.Date.Ticks;

        for (var dayTicks = previousDayTicks; dayTicks <= lastDayTicks; dayTicks += TimeSpan.TicksPerDay)
        {
            foreach (var rule in rules)
            {
                var ruleStartTicks = dayTicks + rule.StartsAt.Ticks;
                var ruleEndTicks = dayTicks + rule.EndsAt.Ticks;

                if (rule.EndsAt < rule.StartsAt)
                {
                    ruleEndTicks += TimeSpan.TicksPerDay;
                }

                var clippedStartTicks = Math.Max(ruleStartTicks, startsAtUtc.Ticks);
                var clippedEndTicks = Math.Min(ruleEndTicks, endsAtUtc.Ticks);

                if (clippedStartTicks < clippedEndTicks)
                {
                    intervals.Add(new RuleInterval(clippedStartTicks, clippedEndTicks, rule));
                }
            }
        }

        return intervals;
    }

    private sealed record RuleInterval(long StartsAtTicks, long EndsAtTicks, BookingPricingRule Rule);
}
