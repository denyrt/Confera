using Confera.Domain.Rooms;
using static Confera.Domain.Tests.BookingTestData;

namespace Confera.Domain.Tests;

public sealed class RoomLifecycleTests
{
    [Fact]
    public void ReplacementPreservesMatchingIdsAndChangesSpellingButKeepsBookingSnapshots()
    {
        var room = CreateRoom();
        var projector = room.Services.Single(x => x.Name == "Projector");
        var wifi = room.Services.Single(x => x.Name == "Wi-Fi");
        var booking = room.Book(Period(At(11), At(15)), At(9), [projector.Id, wifi.Id], InitialRules());
        var version = room.Version;

        room.Update(new RoomDetails(" Renamed ", 60, 2500m, [new("projector", 600m), new("Internet", 400m)]));

        Assert.NotEqual(version, room.Version);
        Assert.Equal("Renamed", room.Name);
        Assert.Equal(60, room.Capacity);
        Assert.Equal(2500m, room.HourlyRate);
        Assert.Equal(projector.Id, room.Services.Single(x => x.Name == "projector").Id);
        Assert.DoesNotContain(room.Services, x => x.Id == wifi.Id);
        Assert.Equal(9400m, booking.TotalPrice);
        Assert.Contains(booking.Services, x => x.ServiceNameSnapshot == "Projector" && x.ServicePriceSnapshot == 500m);
        Assert.Contains(booking.Services, x => x.ServiceNameSnapshot == "Wi-Fi" && x.ServicePriceSnapshot == 300m);
    }

    [Fact]
    public void EquivalentReplacementAndBookingDoNotRotateVersion()
    {
        var room = CreateRoom();
        var version = room.Version;
        room.Update(new RoomDetails(" Room ", 50, 2000.000m, [new("Wi-Fi", 300m), new("Projector", 500.000m)]));
        room.Book(Period(At(11), At(15)), At(9), [], InitialRules());
        Assert.Equal(version, room.Version);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("capacity")]
    [InlineData("rate")]
    [InlineData("service_price")]
    [InlineData("service_spelling")]
    [InlineData("service_remove")]
    public void EveryVisibleMutationRotatesVersion(string change)
    {
        var room = CreateRoom();
        var version = room.Version;
        switch (change)
        {
            case "name": room.SetName("New name"); break;
            case "capacity": room.SetCapacity(60); break;
            case "rate": room.SetHourlyRate(2500m); break;
            case "service_price": room.SetServices([new("Projector", 600m), new("Wi-Fi", 300m)]); break;
            case "service_spelling": room.SetServices([new("PROJECTOR", 500m), new("Wi-Fi", 300m)]); break;
            case "service_remove": room.SetServices([]); break;
        }

        Assert.NotEqual(Guid.Empty, room.Version);
        Assert.NotEqual(version, room.Version);
    }

    [Fact]
    public void InvalidReplacementLeavesEntireAggregateAndVersionUnchanged()
    {
        var room = CreateRoom();
        var version = room.Version;
        Assert.False(RoomDetails.TryCreate("Changed", 60, 2500m,
            [new("Projector", 600m), new("Wi-Fi", 100m)], out var details, out var failure));
        Assert.Null(details);
        Assert.NotNull(failure);
        Assert.Equal(RoomValidationError.InvalidServicePrice, failure.Kind);
        Assert.Throws<RoomValidationException>(() => new RoomDetails("Changed", 60, 2500m,
            [new("Projector", 600m), new(" projector ", 700m)]));
        Assert.Equal("Room", room.Name);
        Assert.Equal(50, room.Capacity);
        Assert.Equal(2000m, room.HourlyRate);
        Assert.Equal(500m, room.Services.Single(x => x.Name == "Projector").Price);
        Assert.Equal(version, room.Version);
    }

    [Fact]
    public void ValidatedReplacementCapturesCallerCollection()
    {
        RoomServiceData[] input = [new("Service", 300m)];
        Assert.True(RoomDetails.TryCreate("Room", 50, 2000m, input, out var details, out var failure));
        Assert.NotNull(details);
        Assert.Null(failure);
        input[0] = new("Other", 400m);
        Assert.Equal("Service", details.Services[0].Name);
        Assert.Throws<NotSupportedException>(() => ((IList<RoomServiceData>)details.Services)[0] = input[0]);
    }

    [Fact]
    public void DeleteRetainsServicesAndIsIdempotentAndPreventsFurtherMutation()
    {
        var room = CreateRoom();
        var original = room.Version;
        room.Delete();
        var deleted = room.Version;
        room.Delete();
        Assert.True(room.IsDeleted);
        Assert.NotEqual(original, deleted);
        Assert.Equal(deleted, room.Version);
        Assert.Equal(2, room.Services.Count);
        Assert.Throws<InvalidOperationException>(() => room.Update(new RoomDetails("Other", 1, 1000m, [])));
        Assert.Throws<InvalidOperationException>(() => room.Book(Period(At(11), At(15)), At(9), [], InitialRules()));
    }

    private static Room CreateRoom()
    {
        var room = new Room("Room", 50, 2000m);
        room.SetServices([new("Projector", 500m), new("Wi-Fi", 300m)]);
        return room;
    }
}
