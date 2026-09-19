namespace Confera.Domain.Bookings;

/// <summary>An expected rejection of booking input, distinct from a broken invariant.</summary>
public sealed class BookingValidationException(BookingValidationError error, string message) : ArgumentException(message)
{
    public BookingValidationError Error { get; } = error;
}
