using System.Diagnostics.CodeAnalysis;
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
    public Guid Version { get; private set; }
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
        Version = Guid.NewGuid();
    }

    public Room(RoomDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        Id = Guid.CreateVersion7();
        Name = details.Name;
        Capacity = details.Capacity;
        HourlyRate = details.HourlyRate;
        Version = Guid.NewGuid();
        ApplyServices(details.Services);
    }

    public void SetName(string name)
    {
        EnsureActive();
        var validated = DomainValidation.RequireText(name, 64, nameof(name));
        if (Name == validated)
        {
            return;
        }

        Name = validated;
        Version = Guid.NewGuid();
    }

    public void SetCapacity(int capacity)
    {
        EnsureActive();
        var validated = DomainValidation.RequirePositive(capacity, nameof(capacity));
        if (Capacity == validated)
        {
            return;
        }

        Capacity = validated;
        Version = Guid.NewGuid();
    }

    public void SetHourlyRate(decimal hourlyRate)
    {
        EnsureActive();
        var validated = DomainValidation.RequireHourlyRate(hourlyRate, nameof(hourlyRate));
        if (HourlyRate == validated)
        {
            return;
        }

        HourlyRate = validated;
        Version = Guid.NewGuid();
    }

    public void SetServices(IEnumerable<RoomServiceData> services)
    {
        EnsureActive();
        if (ApplyServices(RoomDetails.ValidateServices(services)))
        {
            Version = Guid.NewGuid();
        }
    }

    public void Update(RoomDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        EnsureActive();

        var servicesChanged = ApplyServices(details.Services);
        if (!servicesChanged && Name == details.Name && Capacity == details.Capacity && HourlyRate == details.HourlyRate)
        {
            return;
        }

        Name = details.Name;
        Capacity = details.Capacity;
        HourlyRate = details.HourlyRate;
        Version = Guid.NewGuid();
    }

    /// <summary>The caller checks unfinished bookings under the shared room lock.</summary>
    public void Delete()
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        Version = Guid.NewGuid();
    }

    private bool ApplyServices(IReadOnlyList<RoomServiceData> services)
    {
        var newServicesMap = services.ToDictionary(x => NameIdentity.Key(x.Name), StringComparer.Ordinal);
        var changed = false;

        for (var i = _services.Count - 1; i >= 0; --i)
        {
            var existing = _services[i];

            if (newServicesMap.Remove(NameIdentity.Key(existing.Name), out var incoming))
            {
                if (existing.Name != incoming.Name || existing.Price != incoming.Price)
                {
                    existing.Update(incoming.Name, incoming.Price);
                    changed = true;
                }
            }
            else
            {
                _services.RemoveAt(i);
                changed = true;
            }
        }

        var servicesToAdd = newServicesMap.Values.Select(x => new RoomService(Id, x.Name, x.Price));
        foreach (var service in servicesToAdd)
        {
            _services.Add(service);
            changed = true;
        }

        return changed;
    }

    /// <summary>Creates a priced booking; availability and persistence are coordinated by the caller.</summary>
    public Booking Book(
        RentalPeriod period,
        DateTime nowUtc,
        IReadOnlyList<Guid> serviceIds,
        IReadOnlyList<BookingPricingRule> rules)
    {
        if (!TryBook(period, nowUtc, serviceIds, rules, out var booking, out var failure))
        {
            throw failure.ToException();
        }

        return booking;
    }

    /// <summary>Creates a complete booking or returns an expected period, selection, or coverage rejection.</summary>
    /// <remarks>The caller supplies an active room; invalid state, clock, or configuration still throws.</remarks>
    public bool TryBook(
        RentalPeriod period,
        DateTime nowUtc,
        IReadOnlyList<Guid>? serviceIds,
        IReadOnlyList<BookingPricingRule> rules,
        [NotNullWhen(true)] out Booking? booking,
        [NotNullWhen(false)] out BookingValidationFailure? failure)
    {
        booking = null;
        EnsureActive();
        nowUtc = UtcPrecision.Floor(nowUtc);
        if (!BookingValidation.TryValidateBookingPeriod(period, nowUtc, out failure)
            || !BookingValidation.TryValidateServiceIds(serviceIds, out failure))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(rules, nameof(rules));

        if (!TryValidateServiceOwnership(serviceIds, out failure))
        {
            return false;
        }

        var selectedIds = serviceIds.ToHashSet();
        var selectedServices = _services
            .Where(x => selectedIds.Contains(x.Id))
            .Select(x => new RoomServiceData(x.Name, x.Price))
            .ToArray();

        if (!BookingPriceCalculator.TryCalculate(period, HourlyRate, rules, out var segments, out failure))
        {
            return false;
        }

        booking = new Booking(Id, period.StartsAtUtc, period.EndsAtUtc, nowUtc, HourlyRate, selectedServices, segments);
        return true;
    }

    private bool TryValidateServiceOwnership(IEnumerable<Guid> serviceIds,
        [NotNullWhen(false)] out BookingValidationFailure? failure)
    {
        var supportedIds = _services.Select(x => x.Id).ToHashSet();

        if (serviceIds.Any(id => !supportedIds.Contains(id)))
        {
            failure = new(BookingValidationError.InvalidServiceSelection,
                "Selected services are not available in this room.");
            return false;
        }

        failure = null;
        return true;
    }

    private void EnsureActive()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted room cannot be changed or booked.");
        }
    }
}

public readonly record struct RoomServiceData(string Name, decimal Price);
