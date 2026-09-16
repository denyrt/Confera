using Confera.Application.Rooms;

namespace Confera.Application.Availability;

public sealed record AvailabilityPage(IReadOnlyList<RoomResult> Items, int Page, int PageSize, bool HasNextPage);
