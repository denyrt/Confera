using System.Diagnostics.CodeAnalysis;
using Confera.Application.Common;
using Confera.Domain;
using Confera.Domain.Rooms;

namespace Confera.Application.Rooms;

public sealed record RoomCommand(string Name, int Capacity, decimal HourlyRate, IReadOnlyList<RoomServiceData> Services);

public sealed class RoomManagementService(IRoomStore store, TimeProvider timeProvider)
{
    public async Task<Result<RoomState, RoomError>> CreateAsync(
        RoomCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!TryValidate(command, out var details, out var failure))
            {
                return Reject(failure, cancellationToken);
            }

            var room = new Room(details);
            var result = RoomState.FromRoom(room);
            await store.CreateAsync(room, cancellationToken);
            return Result<RoomState, RoomError>.Success(result);
        }
        catch (RoomOperationException error) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<RoomState, RoomError>.Failure(MapPersistenceFailure(error));
        }
    }

    public async Task<Result<RoomState, RoomError>> GetAsync(Guid roomId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (roomId == Guid.Empty)
        {
            return Result<RoomState, RoomError>.Failure(new RoomError.InvalidRequest());
        }

        try
        {
            var room = await store.GetAsync(roomId, cancellationToken);
            if (room is null || room.IsDeleted)
            {
                return Result<RoomState, RoomError>.Failure(new RoomError.NotFound());
            }

            return Result<RoomState, RoomError>.Success(RoomState.FromRoom(room));
        }
        catch (RoomOperationException error) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<RoomState, RoomError>.Failure(MapPersistenceFailure(error));
        }
    }

    public async Task<Result<RoomState, RoomError>> UpdateAsync(
        Guid roomId, RoomCommand command, IReadOnlyList<Guid>? expectedVersions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (roomId == Guid.Empty)
        {
            return Result<RoomState, RoomError>.Failure(new RoomError.InvalidRequest());
        }

        try
        {
            if (!TryValidate(command, out var details, out var failure))
            {
                return Reject(failure, cancellationToken);
            }

            var versions = expectedVersions?.ToArray();

            await using var transaction = await store.BeginAsync(cancellationToken);
            var room = await transaction.GetRoomForUpdateAsync(roomId, cancellationToken);
            if (room is null || room.IsDeleted)
            {
                return Result<RoomState, RoomError>.Failure(new RoomError.NotFound());
            }

            if (details.Capacity < room.Capacity
                && await HasUnfinishedBookingsAsync(transaction, roomId, cancellationToken))
            {
                return Result<RoomState, RoomError>.Failure(new RoomError.HasUnfinishedBookings());
            }

            if (CheckVersion(room, versions) is { } versionError)
            {
                return Result<RoomState, RoomError>.Failure(versionError);
            }

            room.Update(details);
            var result = RoomState.FromRoom(room);
            await transaction.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<RoomState, RoomError>.Success(result);
        }
        catch (RoomOperationException error) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<RoomState, RoomError>.Failure(MapPersistenceFailure(error));
        }
    }

    public async Task<Result<RoomError>> DeleteAsync(Guid roomId, IReadOnlyList<Guid>? expectedVersions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (roomId == Guid.Empty)
        {
            return Result<RoomError>.Failure(new RoomError.InvalidRequest());
        }

        var versions = expectedVersions?.ToArray();

        try
        {
            await using var transaction = await store.BeginAsync(cancellationToken);
            var room = await transaction.GetRoomForUpdateAsync(roomId, cancellationToken);
            if (room is null)
            {
                return Result<RoomError>.Failure(new RoomError.NotFound());
            }

            // A repeated delete confirms the already reached state, even with the old version.
            if (room.IsDeleted)
            {
                return Result<RoomError>.Success();
            }

            if (await HasUnfinishedBookingsAsync(transaction, roomId, cancellationToken))
            {
                return Result<RoomError>.Failure(new RoomError.HasUnfinishedBookings());
            }

            if (CheckVersion(room, versions) is { } versionError)
            {
                return Result<RoomError>.Failure(versionError);
            }

            room.Delete();
            await transaction.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<RoomError>.Success();
        }
        catch (RoomOperationException error) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<RoomError>.Failure(MapPersistenceFailure(error));
        }
    }

    private async Task<bool> HasUnfinishedBookingsAsync(
        IRoomTransaction transaction, Guid roomId, CancellationToken cancellationToken)
    {
        var nowUtc = UtcPrecision.Floor(timeProvider.GetUtcNow().UtcDateTime);
        return await transaction.HasUnfinishedBookingsAsync(roomId, nowUtc, cancellationToken);
    }

    private static bool TryValidate(RoomCommand command, [NotNullWhen(true)] out RoomDetails? details,
        [NotNullWhen(false)] out RoomValidationFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(command);
        return RoomDetails.TryCreate(command.Name, command.Capacity, command.HourlyRate, command.Services, out details, out failure);
    }

    private static Result<RoomState, RoomError> Reject(RoomValidationFailure failure, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Result<RoomState, RoomError>.Failure(new RoomError.InvalidData(failure.Description));
    }

    private static RoomError? CheckVersion(Room room, Guid[]? versions)
    {
        if (versions is null)
        {
            return new RoomError.PreconditionRequired();
        }

        return versions.Contains(room.Version) ? null : new RoomError.VersionMismatch();
    }

    private static RoomError MapPersistenceFailure(RoomOperationException error) => error.Failure switch
    {
        RoomFailure.NameConflict => new RoomError.NameConflict(),
        RoomFailure.VersionMismatch => new RoomError.VersionMismatch(),
        RoomFailure.PersistenceUnavailable => new RoomError.PersistenceUnavailable(),
        _ => throw new InvalidOperationException("Unknown room persistence failure.", error)
    };
}
