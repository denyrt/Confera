namespace Confera.Application.Reports;

public sealed class ReportService(IReportReader reader)
{
    public async Task<RoomReport> GetRoomsAsync(DateTime startsAtUtc, DateTime endsAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var period = ReportPeriod.Create(startsAtUtc, endsAtUtc);
        var rows = await reader.GetRoomsAsync(period, cancellationToken);
        return new RoomReport(period.StartsAtUtc, period.EndsAtUtc, "UAH", rows);
    }

    public async Task<ServiceReport> GetServicesAsync(DateTime startsAtUtc, DateTime endsAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var period = ReportPeriod.Create(startsAtUtc, endsAtUtc);
        var rows = await reader.GetServicesAsync(period, cancellationToken);
        return new ServiceReport(period.StartsAtUtc, period.EndsAtUtc, "UAH", rows);
    }
}
