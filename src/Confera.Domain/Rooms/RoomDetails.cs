using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

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
        if (!TryCreate(name, capacity, hourlyRate, services, out var details, out var failure))
        {
            throw new RoomValidationException(failure.Description, failure.ToArgumentException());
        }

        Name = details.Name;
        Capacity = details.Capacity;
        HourlyRate = details.HourlyRate;
        Services = details.Services;
    }

    private RoomDetails(string name, int capacity, decimal hourlyRate, ReadOnlyCollection<RoomServiceData> services)
    {
        Name = name;
        Capacity = capacity;
        HourlyRate = hourlyRate;
        Services = services;
    }

    public static bool TryCreate(string? name, int capacity, decimal hourlyRate, IEnumerable<RoomServiceData>? services,
        [NotNullWhen(true)] out RoomDetails? details,
        [NotNullWhen(false)] out RoomValidationFailure? failure)
    {
        details = null;
        if (!DomainValidation.TryNormalizeText(name, 64, nameof(name), out var normalizedName, out var nameFailure))
        {
            failure = new(RoomValidationError.InvalidName, nameFailure);
            return false;
        }

        if (DomainValidation.ValidatePositive(capacity, nameof(capacity)) is { } capacityFailure)
        {
            failure = new(RoomValidationError.InvalidCapacity, capacityFailure);
            return false;
        }

        if (DomainValidation.ValidateHourlyRate(hourlyRate, nameof(hourlyRate)) is { } rateFailure)
        {
            failure = new(RoomValidationError.InvalidHourlyRate, rateFailure);
            return false;
        }

        if (!TryValidateServices(services, out var validatedServices, out failure))
        {
            return false;
        }

        details = new RoomDetails(normalizedName, capacity, hourlyRate, Array.AsReadOnly(validatedServices));
        return true;
    }

    internal static RoomServiceData[] ValidateServices(IEnumerable<RoomServiceData> services)
    {
        if (!TryValidateServices(services, out var validated, out var failure))
        {
            throw failure.ToArgumentException();
        }

        return validated;
    }

    private static bool TryValidateServices(IEnumerable<RoomServiceData>? services,
        [NotNullWhen(true)] out RoomServiceData[]? validatedServices,
        [NotNullWhen(false)] out RoomValidationFailure? failure)
    {
        validatedServices = null;
        failure = null;
        if (services is null)
        {
            failure = new(RoomValidationError.MissingServices, InputFailure.Null(nameof(services)));
            return false;
        }

        var validated = new List<RoomServiceData>();
        foreach (var service in services)
        {
            if (!DomainValidation.TryNormalizeText(service.Name, 64, nameof(service.Name), out var name, out var nameFailure))
            {
                failure = new(RoomValidationError.InvalidServiceName, nameFailure);
                return false;
            }

            if (DomainValidation.ValidateServicePrice(service.Price, nameof(service.Price)) is { } priceFailure)
            {
                failure = new(RoomValidationError.InvalidServicePrice, priceFailure);
                return false;
            }

            validated.Add(new RoomServiceData(name, service.Price));
        }

        // A later invalid element takes precedence over duplicate names, as in the original guards.
        if (validated.GroupBy(x => NameIdentity.Key(x.Name), StringComparer.Ordinal).Any(x => x.Count() > 1))
        {
            failure = new(RoomValidationError.DuplicateServiceNames,
                new(InputFailureKind.Invalid, "Service names must be unique within a room.", nameof(services)));
            return false;
        }

        validatedServices = validated.ToArray();
        return true;
    }
}

public sealed class RoomValidationException(string message, Exception innerException) : Exception(message, innerException);
