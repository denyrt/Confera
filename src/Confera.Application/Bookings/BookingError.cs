namespace Confera.Application.Bookings;

public abstract record BookingError
{
    public sealed record InvalidRequest : BookingError;
    public sealed record InvalidPeriod(string Description) : BookingError;
    public sealed record InvalidServiceSelection(string Description) : BookingError;
    public sealed record MissingTariffCoverage(string Description) : BookingError;
    public sealed record RoomNotFound : BookingError;
    public sealed record RoomUnavailable : BookingError;
    public sealed record PersistenceUnavailable : BookingError;
}
