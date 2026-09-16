using Confera.Domain.Rooms;

namespace Confera.Application.Rooms;

public interface IRoomStore
{
    Task<Room?> GetAsync(Guid roomId, CancellationToken cancellationToken);
    Task CreateAsync(Room room, CancellationToken cancellationToken);
    Task<IRoomTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IRoomTransaction : IAsyncDisposable
{
    Task<Room?> GetRoomForUpdateAsync(Guid roomId, CancellationToken cancellationToken);
    Task<bool> HasUnfinishedBookingsAsync(Guid roomId, DateTime nowUtc, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
}
