using Confera.Domain.Bookings;
using Confera.Domain.Rooms;

namespace Confera.Application.Bookings;

public interface IBookingStore
{
    /// <summary>Opens a fresh persistence session and a short transaction without automatic retries.</summary>
    Task<IBookingTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IBookingTransaction : IAsyncDisposable
{
    /// <summary>Locks the room before reading it and its current services; holds the lock until transaction end.</summary>
    Task<Room?> GetRoomForUpdateAsync(Guid roomId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BookingPricingRule>> GetPricingRulesAsync(CancellationToken cancellationToken);
    Task<bool> HasOverlapAsync(Guid roomId, DateTime startsAtUtc, DateTime endsAtUtc, CancellationToken cancellationToken);
    Task SaveAsync(Booking booking, CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
}
