using Confera.Domain.Rooms;

namespace Confera.Domain.Tests;

public sealed class RoomValidationTests
{
    [Theory]
    [InlineData("null_name", "Value cannot be null. (Parameter 'name')")]
    [InlineData("blank_name", "The value cannot be an empty string or composed entirely of whitespace. (Parameter 'name')")]
    [InlineData("long_name", "Value cannot exceed 64 characters. (Parameter 'name')")]
    [InlineData("capacity", "Value must be greater than 0. (Parameter 'capacity')")]
    [InlineData("rate_range", "Value must be between 1000 and 100000. (Parameter 'hourlyRate')")]
    [InlineData("rate_precision", "Value must have at most 3 fractional digits. (Parameter 'hourlyRate')")]
    [InlineData("null_services", "Value cannot be null. (Parameter 'services')")]
    [InlineData("null_service_name", "Value cannot be null. (Parameter 'Name')")]
    [InlineData("service_range", "Value must be between 200 and 20000. (Parameter 'Price')")]
    [InlineData("service_precision", "Value must have at most 3 fractional digits. (Parameter 'Price')")]
    [InlineData("duplicates", "Service names must be unique within a room. (Parameter 'services')")]
    [InlineData("duplicates_and_bad_price", "Value must be between 200 and 20000. (Parameter 'Price')")]
    public void ValidationDetailsPreserveGuardMessagesAndOrder(string scenario, string description)
    {
        var name = scenario switch { "null_name" => null!, "blank_name" => " ", "long_name" => new string('x', 65), _ => "Room" };
        var capacity = scenario is "long_name" or "capacity" ? 0 : 50;
        var rate = scenario switch { "rate_range" => 999m, "rate_precision" => 2000.0001m, _ => 2000m };
        RoomServiceData[] services = scenario switch
        {
            "null_services" => null!,
            "null_service_name" => [new(null!, 100m)],
            "service_range" => [new("Service", 100m)],
            "service_precision" => [new("Service", 300.0001m)],
            "duplicates" => [new("Service", 300m), new(" service ", 400m)],
            "duplicates_and_bad_price" => [new("Service", 300m), new(" service ", 400m), new("Later", 100m)],
            _ => []
        };

        Assert.False(RoomDetails.TryCreate(name, capacity, rate, services, out var details, out var failure));
        Assert.Null(details);
        Assert.NotNull(failure);
        Assert.Equal(description, failure.Description);
        var expectedKind = scenario switch
        {
            "null_name" or "blank_name" or "long_name" => RoomValidationError.InvalidName,
            "capacity" => RoomValidationError.InvalidCapacity,
            "rate_range" or "rate_precision" => RoomValidationError.InvalidHourlyRate,
            "null_services" => RoomValidationError.MissingServices,
            "null_service_name" => RoomValidationError.InvalidServiceName,
            "duplicates" => RoomValidationError.DuplicateServiceNames,
            _ => RoomValidationError.InvalidServicePrice
        };
        Assert.Equal(expectedKind, failure.Kind);

        var error = Assert.Throws<RoomValidationException>(() => new RoomDetails(name, capacity, rate, services));
        Assert.Equal(description, error.Message);
        Assert.Equal(description, error.InnerException!.Message);
    }

    [Fact]
    public void CallerEnumerationFailureIsNotClassifiedAsInvalidRoomData()
    {
        var error = new ArgumentException("Unexpected caller enumeration failure.");
        IEnumerable<RoomServiceData> Services()
        {
            yield return new("Valid", 300m);
            throw error;
        }

        var actual = Assert.Throws<ArgumentException>(() =>
            RoomDetails.TryCreate("Room", 50, 2000m, Services(), out _, out _));
        Assert.Same(error, actual);
    }
}
