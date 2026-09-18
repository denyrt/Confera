namespace Confera.Application.Rooms;

public abstract record RoomError
{
    public sealed record InvalidRequest : RoomError;
    public sealed record InvalidData(string Description) : RoomError;
    public sealed record NotFound : RoomError;
    public sealed record NameConflict : RoomError;
    public sealed record HasUnfinishedBookings : RoomError;
    public sealed record PreconditionRequired : RoomError;
    public sealed record VersionMismatch : RoomError;
    public sealed record PersistenceUnavailable : RoomError;
}
