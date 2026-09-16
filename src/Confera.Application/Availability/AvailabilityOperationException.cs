namespace Confera.Application.Availability;

public enum AvailabilityFailure
{
    InvalidRequest,
    PersistenceUnavailable
}

public sealed class AvailabilityOperationException(AvailabilityFailure failure, Exception? innerException = null)
    : Exception($"Availability search failed: {failure}.", innerException)
{
    public AvailabilityFailure Failure { get; } = failure;
}
