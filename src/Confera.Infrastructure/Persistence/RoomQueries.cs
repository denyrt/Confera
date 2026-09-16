using Confera.Domain.Rooms;
using Microsoft.EntityFrameworkCore;

namespace Confera.Infrastructure.Persistence;

internal static class RoomQueries
{
    internal static async Task<Room?> LockAndLoadAsync(ConferaDbContext db, Guid roomId, CancellationToken cancellationToken)
    {
        var lockedIds = await db.Database.SqlQuery<Guid>(
            $"""SELECT "Id" AS "Value" FROM "Rooms" WHERE "Id" = {roomId} FOR UPDATE""").ToListAsync(cancellationToken);

        // Read children in a fresh statement snapshot AFTER waiting for the parent lock.
        return lockedIds.Count == 0 ? null : await db.Rooms.Include(x => x.Services).AsSingleQuery()
            .SingleAsync(x => x.Id == roomId, cancellationToken);
    }
}
