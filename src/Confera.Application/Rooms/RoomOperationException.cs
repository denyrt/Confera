namespace Confera.Application.Rooms;

public enum RoomFailure
{
    InvalidRequest,
    NotFound,
    NameConflict,
    HasUnfinishedBookings,
    PreconditionRequired,
    VersionMismatch,
    PersistenceUnavailable
}

public sealed class RoomOperationException(RoomFailure failure, Exception? innerException = null)
    : Exception("The room operation could not be completed.", innerException)
{
    public RoomFailure Failure { get; } = failure;
}
