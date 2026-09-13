using Confera.Domain.Bookings;

namespace Confera.Domain.Rooms;

public sealed class Room
{
    private readonly List<RoomService> _services = [];
    private readonly List<Booking> _booking = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public int Capacity { get; private set; }
    public decimal HourlyRate { get; private set; }
    public IReadOnlyCollection<RoomService> Services => _services;
    public IReadOnlyCollection<Booking> Bookings => _booking;

    private Room()
    {
        Name = null!;
    }

    public Room(string name, int capacity, decimal hourlyRate)
    {
        Id = Guid.CreateVersion7();
        Name = DomainValidation.RequireText(name, 64, nameof(name));
        Capacity = DomainValidation.RequirePositive(capacity, nameof(capacity));
        HourlyRate = DomainValidation.RequirePositive(hourlyRate, nameof(hourlyRate));
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
        HourlyRate = DomainValidation.RequirePositive(hourlyRate, nameof(hourlyRate));
    }

    public void SetServices(IEnumerable<RoomServiceData> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var validated = services
            .Select(x => new RoomServiceData(
                DomainValidation.RequireText(x.Name, 64, nameof(x.Name)),
                DomainValidation.RequirePositive(x.Price, nameof(x.Price))))
            .ToArray();

        ThrowIfServiceNamesNotUnique(validated, nameof(services));

        var newServicesMap = validated.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

        for (var i = _services.Count - 1; i >= 0; --i)
        {
            var existing = _services[i];

            if (newServicesMap.Remove(existing.Name, out var incoming))
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

    public Booking Book(
        DateTime startDate,
        DateTime endDate,
        DateTime nowUtc,
        IReadOnlyList<Guid> serviceIds,
        IReadOnlyList<BookingPricingRule> rules)
    {
        DomainValidation.RequireBookingPeriod(startDate, endDate, nowUtc);
        ArgumentNullException.ThrowIfNull(serviceIds, nameof(serviceIds));
        ArgumentNullException.ThrowIfNull(rules, nameof(rules));

        EnsureUniqueIds(serviceIds, nameof(serviceIds));
        EnsureServiceIdsExists(serviceIds, nameof(serviceIds));

        throw new NotImplementedException();
    }

    private void EnsureServiceIdsExists(IEnumerable<Guid> serviceIds, string parameterName)
    {
        var supportedIds = _services.Select(x => x.Id).ToHashSet();
        if (serviceIds.Any(id => !supportedIds.Contains(id)))
        {
            throw new ArgumentException("Selected services are not available in this room.", parameterName);
        }
    }

    private static void ThrowIfServiceNamesNotUnique(IReadOnlyCollection<RoomServiceData> services, string parameterName)
    {
        if (services
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Service names must be unique within a room.", parameterName);
        }
    }

    private static void EnsureUniqueIds(IReadOnlyList<Guid> serviceIds, string parameterName)
    {
        if (serviceIds.ToHashSet().Count != serviceIds.Count)
        {
            throw new ArgumentException("Duplicate ids cannot be used.", parameterName);
        }
    }
}

public readonly record struct RoomServiceData(string Name, decimal Price);
