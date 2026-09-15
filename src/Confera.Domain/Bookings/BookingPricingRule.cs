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
        StartsAt = DomainValidation.RequireDailyTime(startsAt, nameof(startsAt));
        EndsAt = DomainValidation.RequireDailyTime(endsAt, nameof(endsAt));
        Multiplier = DomainValidation.RequireMultiplier(multiplier, nameof(multiplier));
        Priority = priority;
    }

    /// <summary>Every rule in the complete configuration has a unique priority.</summary>
    public static void ValidateSet(IReadOnlyList<BookingPricingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule, nameof(rules));
        }

        if (rules.Select(rule => rule.Priority).Distinct().Count() != rules.Count)
        {
            throw new ArgumentException("Pricing rule priorities must be globally unique.", nameof(rules));
        }
    }
}
