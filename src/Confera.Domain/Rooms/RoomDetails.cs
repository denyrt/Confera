namespace Confera.Domain.Rooms;

/// <summary>Validated, captured replacement data; construction never mutates a room.</summary>
public sealed class RoomDetails
{
    public string Name { get; }
    public int Capacity { get; }
    public decimal HourlyRate { get; }
    public IReadOnlyList<RoomServiceData> Services { get; }

    public RoomDetails(string name, int capacity, decimal hourlyRate, IEnumerable<RoomServiceData> services)
    {
        try
        {
            Name = DomainValidation.RequireText(name, 64, nameof(name));
            Capacity = DomainValidation.RequirePositive(capacity, nameof(capacity));
            HourlyRate = DomainValidation.RequireHourlyRate(hourlyRate, nameof(hourlyRate));
            Services = Array.AsReadOnly(ValidateServices(services));
        }
        catch (ArgumentException error)
        {
            // Only known input guards are translated; unexpected application errors stay internal.
            throw new RoomValidationException(error.Message, error);
        }
    }

    internal static RoomServiceData[] ValidateServices(IEnumerable<RoomServiceData> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var validated = services.Select(x => new RoomServiceData(
            DomainValidation.RequireText(x.Name, 64, nameof(x.Name)),
            DomainValidation.RequireServicePrice(x.Price, nameof(x.Price)))).ToArray();

        if (validated.GroupBy(x => NameIdentity.Key(x.Name), StringComparer.Ordinal).Any(x => x.Count() > 1))
        {
            throw new ArgumentException("Service names must be unique within a room.", nameof(services));
        }

        return validated;
    }
}

public sealed class RoomValidationException(string message, Exception innerException) : Exception(message, innerException);
