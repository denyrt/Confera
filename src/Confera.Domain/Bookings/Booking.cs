namespace Confera.Domain.Bookings;

public sealed class Booking
{
    private readonly List<BookedRoomServiceSnapshot> _services = [];
    private readonly List<BookingPriceSegment> _priceSegments = [];

    public Guid Id { get; private set; }
    public Guid RoomId { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public decimal HourlyRateSnapshot { get; private set; }
    public decimal TotalPrice { get; private set; }
    public IReadOnlyCollection<BookedRoomServiceSnapshot> Services => _services;
    public IReadOnlyCollection<BookingPriceSegment> PriceSegments => _priceSegments;

    private Booking()
    {
    }

    internal Booking(
        Guid roomId,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        DateTime nowUtc,
        decimal hourlyRateSnapshot,
        decimal totalPrice,
        IReadOnlyCollection<BookedRoomServiceSnapshot> services,
        IReadOnlyCollection<BookingPriceSegment> priceSegments)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));
        ArgumentNullException.ThrowIfNull(priceSegments, nameof(priceSegments));

        DomainValidation.RequireBookingPeriod(startsAtUtc, endsAtUtc, nowUtc);

        if (priceSegments.Count == 0)
        {
            throw new ArgumentException("", nameof(priceSegments));
        }

        Id = Guid.CreateVersion7();
        RoomId = DomainValidation.RequireGuid(roomId, nameof(roomId));
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        CreatedAtUtc = nowUtc;
        HourlyRateSnapshot = DomainValidation.RequirePositive(hourlyRateSnapshot, nameof(hourlyRateSnapshot));
        TotalPrice = DomainValidation.RequirePositive(totalPrice, nameof(totalPrice));
        _services = [.. services];
        _priceSegments = [.. priceSegments];
    }
}
