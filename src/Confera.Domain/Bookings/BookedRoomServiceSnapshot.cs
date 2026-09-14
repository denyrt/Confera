namespace Confera.Domain.Bookings;

public sealed class BookedRoomServiceSnapshot
{
    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public string ServiceNameSnapshot { get; private set; }
    public decimal ServicePriceSnapshot { get; private set; }

    private BookedRoomServiceSnapshot()
    {
        ServiceNameSnapshot = null!;
    }

    internal BookedRoomServiceSnapshot(Guid bookingId, string serviceNameSnapshot, decimal servicePriceSnapshot)
    {
        Id = Guid.CreateVersion7();
        BookingId = DomainValidation.RequireGuid(bookingId, nameof(bookingId));
        ServiceNameSnapshot = DomainValidation.RequireText(serviceNameSnapshot, 64, nameof(serviceNameSnapshot));
        ServicePriceSnapshot = DomainValidation.RequireMoney(servicePriceSnapshot, nameof(servicePriceSnapshot));
    }
}
