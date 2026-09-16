using Confera.Domain.Rooms;

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
    public IReadOnlyCollection<BookedRoomServiceSnapshot> Services => _services.AsReadOnly();
    public IReadOnlyCollection<BookingPriceSegment> PriceSegments => _priceSegments.AsReadOnly();

    private Booking()
    {
    }

    internal Booking(
        Guid roomId,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        DateTime nowUtc,
        decimal hourlyRateSnapshot,
        IReadOnlyCollection<RoomServiceData> services,
        IReadOnlyList<BookingRentalSegment> priceSegments)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));
        ArgumentNullException.ThrowIfNull(priceSegments, nameof(priceSegments));

        BookingValidation.RequireBookingPeriod(RentalPeriod.Create(startsAtUtc, endsAtUtc), nowUtc);
        RequireContinuousCoverage(priceSegments, startsAtUtc, endsAtUtc);
        RequireMatchingHourlyRate(priceSegments, hourlyRateSnapshot);

        Id = Guid.CreateVersion7();
        RoomId = DomainValidation.RequireGuid(roomId, nameof(roomId));
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        CreatedAtUtc = nowUtc;
        HourlyRateSnapshot = DomainValidation.RequireHourlyRate(hourlyRateSnapshot, nameof(hourlyRateSnapshot));

        _services.AddRange(services.Select(x => new BookedRoomServiceSnapshot(Id, x.Name, x.Price)));
        _priceSegments.AddRange(priceSegments.Select(x => new BookingPriceSegment(Id, x)));

        var rentalPrice = _priceSegments.Sum(x => x.Price);
        var servicePrice = _services.Sum(x => x.ServicePriceSnapshot);
        TotalPrice = DomainValidation.RequireTotal(rentalPrice + servicePrice, nameof(TotalPrice));
    }

    private static void RequireContinuousCoverage(
        IReadOnlyList<BookingRentalSegment> priceSegments,
        DateTime startsAtUtc,
        DateTime endsAtUtc)
    {
        if (priceSegments.Count == 0)
        {
            throw new ArgumentException("Booking must contain rental segments.", nameof(priceSegments));
        }

        var nextStart = startsAtUtc;
        foreach (var segment in priceSegments)
        {
            ArgumentNullException.ThrowIfNull(segment, nameof(priceSegments));

            if (segment.StartsAtUtc != nextStart || segment.EndsAtUtc > endsAtUtc)
            {
                throw new ArgumentException("Rental segments must cover the booking in order without gaps or overlaps.", nameof(priceSegments));
            }

            nextStart = segment.EndsAtUtc;
        }

        if (nextStart != endsAtUtc)
        {
            throw new ArgumentException("Rental segments must cover the entire booking.", nameof(priceSegments));
        }
    }

    private static void RequireMatchingHourlyRate(
        IReadOnlyList<BookingRentalSegment> priceSegments,
        decimal hourlyRateSnapshot)
    {
        if (priceSegments.Any(segment => segment.HourlyRate != hourlyRateSnapshot))
        {
            throw new ArgumentException("Every rental segment must use the booking's hourly rate.", nameof(priceSegments));
        }
    }
}
