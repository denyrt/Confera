namespace Confera.Domain.Bookings;

public enum BookingValidationError
{
    InvalidPeriod,
    InvalidServiceSelection,
    MissingTariffCoverage
}

/// <summary>An expected input rejection, independent of HTTP and persistence.</summary>
public sealed record BookingValidationFailure(BookingValidationError Kind, string Description)
{
    internal BookingValidationException ToException() => new(Kind, Description);
}
