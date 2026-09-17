namespace Confera.Application.Availability;

public abstract record AvailabilityError
{
    public sealed record InvalidRequest : AvailabilityError;
    public sealed record InvalidPeriod(string Description) : AvailabilityError;
    public sealed record PersistenceUnavailable : AvailabilityError;
}
