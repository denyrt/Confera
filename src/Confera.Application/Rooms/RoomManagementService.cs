using Confera.Domain;
using Confera.Domain.Rooms;

namespace Confera.Application.Rooms;

public sealed record RoomCommand(string Name, int Capacity, decimal HourlyRate, IReadOnlyList<RoomServiceData> Services);

public sealed class RoomManagementService(IRoomStore store, TimeProvider timeProvider)
{
    public async Task<RoomState> CreateAsync(RoomCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var room = new Room(Validate(command));
        var result = RoomState.FromRoom(room);
        await store.CreateAsync(room, cancellationToken);
        return result;
    }

    public async Task<RoomState> GetAsync(Guid roomId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireRoomId(roomId);
        var room = await store.GetAsync(roomId, cancellationToken);
        RequireActiveRoom(room);
        return RoomState.FromRoom(room!);
    }

    public async Task<RoomState> UpdateAsync(Guid roomId, RoomCommand command, IReadOnlyList<Guid>? expectedVersions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireRoomId(roomId);
        var details = Validate(command);
        var versions = expectedVersions?.ToArray();

        await using var transaction = await store.BeginAsync(cancellationToken);
        var room = await transaction.GetRoomForUpdateAsync(roomId, cancellationToken);
        RequireActiveRoom(room);

        if (details.Capacity < room!.Capacity)
        {
            await RequireNoUnfinishedBookingsAsync(transaction, roomId, cancellationToken);
        }

        RequireVersion(room, versions);
        room.Update(details);
        var result = RoomState.FromRoom(room);
        await transaction.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task DeleteAsync(Guid roomId, IReadOnlyList<Guid>? expectedVersions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireRoomId(roomId);
        var versions = expectedVersions?.ToArray();

        await using var transaction = await store.BeginAsync(cancellationToken);
        var room = await transaction.GetRoomForUpdateAsync(roomId, cancellationToken)
            ?? throw new RoomOperationException(RoomFailure.NotFound);

        // A repeated delete confirms the already reached state, even with the old version.
        if (room.IsDeleted)
        {
            return;
        }

        await RequireNoUnfinishedBookingsAsync(transaction, roomId, cancellationToken);
        RequireVersion(room, versions);
        room.Delete();
        await transaction.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RequireNoUnfinishedBookingsAsync(IRoomTransaction transaction, Guid roomId, CancellationToken cancellationToken)
    {
        var nowUtc = UtcPrecision.Floor(timeProvider.GetUtcNow().UtcDateTime);
        if (await transaction.HasUnfinishedBookingsAsync(roomId, nowUtc, cancellationToken))
        {
            throw new RoomOperationException(RoomFailure.HasUnfinishedBookings);
        }
    }

    private static RoomDetails Validate(RoomCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new RoomDetails(command.Name, command.Capacity, command.HourlyRate, command.Services);
    }

    private static void RequireRoomId(Guid roomId)
    {
        if (roomId == Guid.Empty)
        {
            throw new RoomOperationException(RoomFailure.InvalidRequest);
        }
    }

    private static void RequireActiveRoom(Room? room)
    {
        if (room is null || room.IsDeleted)
        {
            throw new RoomOperationException(RoomFailure.NotFound);
        }
    }

    private static void RequireVersion(Room room, Guid[]? versions)
    {
        if (versions is null)
        {
            throw new RoomOperationException(RoomFailure.PreconditionRequired);
        }

        if (!versions.Contains(room.Version))
        {
            throw new RoomOperationException(RoomFailure.VersionMismatch);
        }
    }
}
