namespace Confera.Application.Bookings;

public enum BookingFailure
{
    InvalidRequest,
    RoomNotFound,
    RoomUnavailable,
    PersistenceUnavailable
}

public sealed class BookingOperationException(BookingFailure failure, Exception? innerException = null)
    : Exception($"Booking operation failed: {failure}.", innerException)
{
    public BookingFailure Failure { get; } = failure;
}
