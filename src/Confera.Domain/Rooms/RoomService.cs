namespace Confera.Domain.Rooms;

public sealed class RoomService
{
    public Guid Id { get; private set; }
    public Guid RoomId { get; private set; }
    public string Name { get; private set; }
    public decimal Price { get; private set; }

    private RoomService()
    {
        Name = null!;
    }

    internal RoomService(Guid roomId, string name, decimal price)
    {
        Id = Guid.CreateVersion7();
        RoomId = DomainValidation.RequireGuid(roomId, nameof(roomId));
        Name = DomainValidation.RequireText(name, maxLength: 64, nameof(name));
        Price = DomainValidation.RequireMoney(price, nameof(price));
    }

    internal void UpdatePrice(decimal price)
    {
        Price = DomainValidation.RequireMoney(price, nameof(price));
    }
}
