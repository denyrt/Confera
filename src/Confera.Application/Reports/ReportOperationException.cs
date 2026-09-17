namespace Confera.Application.Reports;

public enum ReportFailure { InvalidRequest, InvalidPeriod, PersistenceUnavailable }

public sealed class ReportOperationException(ReportFailure failure, Exception? innerException = null)
    : Exception($"Report operation failed: {failure}.", innerException)
{
    public ReportFailure Failure { get; } = failure;
}
