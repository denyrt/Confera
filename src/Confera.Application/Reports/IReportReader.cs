namespace Confera.Application.Reports;

public interface IReportReader
{
    Task<IReadOnlyList<RoomReportRow>> GetRoomsAsync(ReportPeriod period, CancellationToken cancellationToken);

    Task<IReadOnlyList<ServiceReportRow>> GetServicesAsync(ReportPeriod period, CancellationToken cancellationToken);
}
