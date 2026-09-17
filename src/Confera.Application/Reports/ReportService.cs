using Confera.Application.Common;

namespace Confera.Application.Reports;

public sealed class ReportService(IReportReader reader)
{
    public async Task<Result<RoomReport, ReportError>> GetRoomsAsync(DateTime startsAtUtc, DateTime endsAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReportPeriod.TryCreate(startsAtUtc, endsAtUtc, out var period))
        {
            return Result<RoomReport, ReportError>.Failure(ReportError.InvalidPeriod);
        }

        try
        {
            var rows = await reader.GetRoomsAsync(period, cancellationToken);
            return Result<RoomReport, ReportError>.Success(new RoomReport(period.StartsAtUtc, period.EndsAtUtc, "UAH", rows));
        }
        catch (ReportOperationException error) when (!cancellationToken.IsCancellationRequested
            && error.Failure == ReportFailure.PersistenceUnavailable)
        {
            return Result<RoomReport, ReportError>.Failure(ReportError.PersistenceUnavailable);
        }
    }

    public async Task<Result<ServiceReport, ReportError>> GetServicesAsync(DateTime startsAtUtc, DateTime endsAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReportPeriod.TryCreate(startsAtUtc, endsAtUtc, out var period))
        {
            return Result<ServiceReport, ReportError>.Failure(ReportError.InvalidPeriod);
        }

        try
        {
            var rows = await reader.GetServicesAsync(period, cancellationToken);
            return Result<ServiceReport, ReportError>.Success(new ServiceReport(period.StartsAtUtc, period.EndsAtUtc, "UAH", rows));
        }
        catch (ReportOperationException error) when (!cancellationToken.IsCancellationRequested
            && error.Failure == ReportFailure.PersistenceUnavailable)
        {
            return Result<ServiceReport, ReportError>.Failure(ReportError.PersistenceUnavailable);
        }
    }
}
