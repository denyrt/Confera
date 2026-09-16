using Confera.Domain.Rooms;

namespace Confera.Application.Rooms;

public sealed record RoomServiceResult(Guid Id, string Name, decimal Price);

public sealed record RoomResult(Guid Id, string Name, int Capacity, decimal HourlyRate,
    string Currency, IReadOnlyList<RoomServiceResult> Services);

// Keep persistence versions separate from the public JSON representation.
public sealed record RoomState(RoomResult Room, Guid Version)
{
    public static RoomState FromRoom(Room room) => new(
        new RoomResult(room.Id, room.Name, room.Capacity, room.HourlyRate, "UAH",
            room.Services.OrderBy(x => x.Id).Select(x => new RoomServiceResult(x.Id, x.Name, x.Price)).ToArray()),
        room.Version);
}
