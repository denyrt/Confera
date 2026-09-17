namespace Confera.Application.Reports;

public sealed record ReportPeriod
{
    public DateTime StartsAtUtc { get; }
    public DateTime EndsAtUtc { get; }

    private ReportPeriod(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
    }

    public static ReportPeriod Create(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc
            || startsAtUtc.Ticks % 10 != 0 || endsAtUtc.Ticks % 10 != 0 || endsAtUtc <= startsAtUtc)
        {
            throw new ReportOperationException(ReportFailure.InvalidPeriod);
        }

        return new ReportPeriod(startsAtUtc, endsAtUtc);
    }
}
