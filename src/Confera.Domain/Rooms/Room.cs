using Confera.Domain.Bookings;

namespace Confera.Domain.Rooms;

public sealed class Room
{
    private readonly List<RoomService> _services = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public int Capacity { get; private set; }
    public decimal HourlyRate { get; private set; }
    public bool IsDeleted { get; private set; }
    public IReadOnlyCollection<RoomService> Services => _services.AsReadOnly();

    private Room()
    {
        Name = null!;
    }

    public Room(string name, int capacity, decimal hourlyRate)
    {
        Id = Guid.CreateVersion7();
        Name = DomainValidation.RequireText(name, 64, nameof(name));
        Capacity = DomainValidation.RequirePositive(capacity, nameof(capacity));
        HourlyRate = DomainValidation.RequireHourlyRate(hourlyRate, nameof(hourlyRate));
    }

    public void SetName(string name)
    {
        Name = DomainValidation.RequireText(name, 64, nameof(name));
    }

    public void SetCapacity(int capacity)
    {
        Capacity = DomainValidation.RequirePositive(capacity, nameof(capacity));
    }

    public void SetHourlyRate(decimal hourlyRate)
    {
        HourlyRate = DomainValidation.RequireHourlyRate(hourlyRate, nameof(hourlyRate));
    }

    public void SetServices(IEnumerable<RoomServiceData> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var validated = services
            .Select(x => new RoomServiceData(
                DomainValidation.RequireText(x.Name, 64, nameof(x.Name)),
                DomainValidation.RequireServicePrice(x.Price, nameof(x.Price))))
            .ToArray();

        ThrowIfServiceNamesNotUnique(validated, nameof(services));

        var newServicesMap = validated.ToDictionary(x => NameIdentity.Key(x.Name), StringComparer.Ordinal);

        for (var i = _services.Count - 1; i >= 0; --i)
        {
            var existing = _services[i];

            if (newServicesMap.Remove(NameIdentity.Key(existing.Name), out var incoming))
            {
                existing.UpdatePrice(incoming.Price);
            }
            else
            {
                _services.RemoveAt(i);
            }
        }

        var servicesToAdd = newServicesMap.Values.Select(x => new RoomService(Id, x.Name, x.Price));
        foreach (var service in servicesToAdd)
        {
            _services.Add(service);
        }
    }

    /// <summary>Creates a priced booking; availability and persistence are coordinated by the caller.</summary>
    public Booking Book(
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        DateTime nowUtc,
        IReadOnlyList<Guid> serviceIds,
        IReadOnlyList<BookingPricingRule> rules)
    {
        nowUtc = UtcPrecision.Floor(nowUtc);
        BookingValidation.RequireBookingPeriod(startsAtUtc, endsAtUtc, nowUtc);
        BookingValidation.RequireServiceIds(serviceIds);
        ArgumentNullException.ThrowIfNull(rules, nameof(rules));

        EnsureServiceIdsExist(serviceIds);

        var selectedIds = serviceIds.ToHashSet();
        var selectedServices = _services
            .Where(x => selectedIds.Contains(x.Id))
            .Select(x => new RoomServiceData(x.Name, x.Price))
            .ToArray();

        var segments = BookingPriceCalculator.Calculate(startsAtUtc, endsAtUtc, HourlyRate, rules);

        return new Booking(Id, startsAtUtc, endsAtUtc, nowUtc, HourlyRate, selectedServices, segments);
    }

    private void EnsureServiceIdsExist(IEnumerable<Guid> serviceIds)
    {
        var supportedIds = _services.Select(x => x.Id).ToHashSet();

        if (serviceIds.Any(id => !supportedIds.Contains(id)))
        {
            throw new BookingValidationException(BookingValidationError.InvalidServiceSelection,
                "Selected services are not available in this room.");
        }
    }

    private static void ThrowIfServiceNamesNotUnique(IReadOnlyCollection<RoomServiceData> services, string parameterName)
    {
        if (services
            .GroupBy(x => NameIdentity.Key(x.Name), StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Service names must be unique within a room.", parameterName);
        }
    }
}

public readonly record struct RoomServiceData(string Name, decimal Price);
