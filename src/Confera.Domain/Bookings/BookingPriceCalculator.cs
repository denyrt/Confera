namespace Confera.Domain.Bookings;

public static class BookingPriceCalculator
{
    public static IReadOnlyList<BookingRentalSegment> Calculate(
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        decimal hourlyRate,
        IReadOnlyList<BookingPricingRule> rules)
    {
        DomainValidation.RequireRentalPeriod(startsAtUtc, endsAtUtc);
        DomainValidation.RequireHourlyRate(hourlyRate, nameof(hourlyRate));
        BookingPricingRule.ValidateSet(rules);

        var intervals = ExpandRules(startsAtUtc, endsAtUtc, rules);
        var boundaries = GetSegmentBoundaries(startsAtUtc, endsAtUtc, intervals);
        var segments = new List<BookingRentalSegment>();

        for (var i = 0; i < boundaries.Length - 1; i++)
        {
            var segmentStartTicks = boundaries[i];
            var segmentEndTicks = boundaries[i + 1];
            var rule = RequireCoveringRule(intervals, segmentStartTicks, segmentEndTicks, nameof(rules));

            segments.Add(new BookingRentalSegment(
                new DateTime(segmentStartTicks, DateTimeKind.Utc),
                new DateTime(segmentEndTicks, DateTimeKind.Utc),
                rule.Code,
                rule.Multiplier,
                hourlyRate));
        }

        return segments.AsReadOnly();
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

    private static BookingPricingRule RequireCoveringRule(
        IReadOnlyList<RuleInterval> intervals,
        long segmentStartTicks,
        long segmentEndTicks,
        string parameterName)
    {
        var rule = intervals
            .Where(interval => interval.StartsAtTicks <= segmentStartTicks
                && interval.EndsAtTicks >= segmentEndTicks)
            .Select(interval => interval.Rule)
            .MaxBy(rule => rule.Priority);

        if (rule is null)
        {
            throw new ArgumentException("The entire booking period must be covered by pricing rules.", parameterName);
        }

        return rule;
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
