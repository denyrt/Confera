namespace Confera.Domain.Bookings;

public sealed class BookingPricingRule
{
    public Guid Id { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public TimeOnly StartsAt { get; private set; }
    public TimeOnly EndsAt { get; private set; }
    public decimal Multiplier { get; private set; }
    public int Priority { get; private set; }

    private BookingPricingRule()
    {
        Code = null!;
        Name = null!;
    }

    public BookingPricingRule(
        string code,
        string name,
        TimeOnly startsAt,
        TimeOnly endsAt,
        decimal multiplier,
        int priority)
    {
        if (startsAt == endsAt)
        {
            throw new ArgumentException("Pricing rule start and end times must be different.", nameof(startsAt));
        }

        Id = Guid.CreateVersion7();
        Code = DomainValidation.RequireText(code, 64, nameof(code));
        Name = DomainValidation.RequireText(name, 64, nameof(name));
        StartsAt = startsAt;
        EndsAt = endsAt;
        Multiplier = DomainValidation.RequirePositive(multiplier, nameof(multiplier));
        Priority = priority;
    }

    /// <summary>Rules with the same priority may only cover disjoint daily intervals.</summary>
    public static void ValidateSet(IReadOnlyList<BookingPricingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule, nameof(rules));
        }

        for (var i = 0; i < rules.Count; i++)
        {
            for (var j = i + 1; j < rules.Count; j++)
            {
                var firstRule = rules[i];
                var secondRule = rules[j];

                if (firstRule.Priority == secondRule.Priority && Overlaps(firstRule, secondRule))
                {
                    throw new ArgumentException("Overlapping pricing rules must have different priorities.", nameof(rules));
                }
            }
        }
    }

    private static bool Overlaps(BookingPricingRule first, BookingPricingRule second)
    {
        foreach (var firstInterval in first.GetDailyIntervals())
        {
            foreach (var secondInterval in second.GetDailyIntervals())
            {
                if (firstInterval.Overlaps(secondInterval))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private IEnumerable<DailyInterval> GetDailyIntervals()
    {
        if (StartsAt < EndsAt)
        {
            yield return new DailyInterval(StartsAt.Ticks, EndsAt.Ticks);
            yield break;
        }

        // For example, 22:00-06:00 becomes [22:00, 24:00) and [00:00, 06:00).
        yield return new DailyInterval(StartsAt.Ticks, TimeSpan.TicksPerDay);

        if (EndsAt > TimeOnly.MinValue)
        {
            yield return new DailyInterval(0, EndsAt.Ticks);
        }
    }

    private readonly record struct DailyInterval(long StartsAtTicks, long EndsAtTicks)
    {
        public bool Overlaps(DailyInterval other) =>
            StartsAtTicks < other.EndsAtTicks && EndsAtTicks > other.StartsAtTicks;
    }
}
