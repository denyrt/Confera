using System.Diagnostics.CodeAnalysis;

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
        return TryCreate(startsAtUtc, endsAtUtc, out var period)
            ? period
            : throw new ReportOperationException(ReportFailure.InvalidPeriod);
    }

    public static bool TryCreate(DateTime startsAtUtc, DateTime endsAtUtc,
        [NotNullWhen(true)] out ReportPeriod? period)
    {
        period = null;
        if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc
            || startsAtUtc.Ticks % 10 != 0 || endsAtUtc.Ticks % 10 != 0 || endsAtUtc <= startsAtUtc)
        {
            return false;
        }

        period = new ReportPeriod(startsAtUtc, endsAtUtc);
        return true;
    }
}
