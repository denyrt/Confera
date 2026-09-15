using System.Globalization;
using Confera.Domain.Rooms;

namespace Confera.Domain.Tests;

public sealed class RoomMoneyTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1000.0001")]
    [InlineData("100001")]
    public void Room_RejectsInvalidMoneyAtAllEntryPoints(string input)
    {
        var price = decimal.Parse(input, CultureInfo.InvariantCulture);
        var room = new Room("Room", 1, 1000m);
        room.SetServices([new("Projector", 500m)]);
        var serviceId = room.Services.Single().Id;

        Assert.ThrowsAny<ArgumentException>(() => new Room("Invalid", 1, price));
        Assert.ThrowsAny<ArgumentException>(() => room.SetHourlyRate(price));
        Assert.ThrowsAny<ArgumentException>(() => room.SetServices([new("Projector", price)]));
        Assert.Equal(1000m, room.HourlyRate);
        Assert.Equal(serviceId, room.Services.Single().Id);
        Assert.Equal(500m, room.Services.Single().Price);
    }

    [Theory]
    [InlineData("1000.001")]
    [InlineData("2000.234")]
    [InlineData("1000.0000")]
    public void Room_AcceptsThreeDigitMoneyAndTrailingZeros(string input)
    {
        var price = decimal.Parse(input, CultureInfo.InvariantCulture);
        var room = new Room("Room", 1, price);
        room.SetHourlyRate(price);
        room.SetServices([new("Projector", price)]);

        Assert.Equal(price, room.HourlyRate);
        Assert.Equal(price, room.Services.Single().Price);
    }

    [Fact]
    public void SetServices_ValidatesEntireReplacementBeforeMutatingPrices()
    {
        var room = new Room("Room", 1, 1000m);
        room.SetServices([new("Projector", 500m), new("Wi-Fi", 300m)]);

        Assert.Throws<ArgumentException>(() => room.SetServices([new("Projector", 900m), new("Sound", 500.0001m)]));

        Assert.Equal(new[] { 500m, 300m }, room.Services.Select(x => x.Price));
        Assert.Equal(new[] { "Projector", "Wi-Fi" }, room.Services.Select(x => x.Name));
    }
}
