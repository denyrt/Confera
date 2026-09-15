using System.Globalization;
using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using static Confera.Domain.Tests.BookingTestData;

namespace Confera.Domain.Tests;

public sealed class PersistenceContractTests
{
    [Fact]
    public void NameHelpersRejectNullInsteadOfProducingEmptyKeys()
    {
        Assert.Throws<ArgumentNullException>("value", () => NameIdentity.Trim(null!));
        Assert.Throws<ArgumentNullException>("value", () => NameIdentity.Key(null!));
    }

    [Theory]
    [InlineData("1000", "200", "0.50")]
    [InlineData("100000", "20000", "2.00")]
    [InlineData("1000.0010", "200.0010", "1.1500")]
    public void InclusiveBoundsAndTrailingZerosAreAccepted(string rateText, string priceText, string multiplierText)
    {
        var rate = decimal.Parse(rateText, CultureInfo.InvariantCulture);
        var price = decimal.Parse(priceText, CultureInfo.InvariantCulture);
        var multiplier = decimal.Parse(multiplierText, CultureInfo.InvariantCulture);
        var room = new Room("Room", 1, rate);
        room.SetServices([new("Service", price)]);
        var booking = room.Book(At(10), At(11), At(9), room.Services.Select(x => x.Id).ToArray(), [Rule("Day", 9, 18, multiplier)]);
        Assert.Equal(decimal.Round(rate * multiplier, 3, MidpointRounding.AwayFromZero) + price, booking.TotalPrice);
        Assert.False(room.IsDeleted);
    }

    [Theory]
    [InlineData("0.49")]
    [InlineData("2.01")]
    [InlineData("1.001")]
    public void InvalidMultiplierIsRejected(string text) =>
        Assert.ThrowsAny<ArgumentException>(() => Rule("Day", 9, 18, decimal.Parse(text, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("199.999")]
    [InlineData("20000.001")]
    [InlineData("200.0001")]
    public void InvalidServiceReplacementLeavesNamesIdsAndPricesUnchanged(string text)
    {
        var room = new Room("Room", 1, 1000m);
        room.SetServices([new("First", 500m), new("Second", 300m)]);
        var before = room.Services.Select(x => (x.Id, x.Name, x.Price)).ToArray();
        Assert.ThrowsAny<ArgumentException>(() => room.SetServices([new("First", 900m), new("Third", decimal.Parse(text, CultureInfo.InvariantCulture))]));
        Assert.Equal(before, room.Services.Select(x => (x.Id, x.Name, x.Price)));
    }

    [Theory]
    [InlineData("\u00a0 Україна їєґ \u2009", "УКРАЇНА ЇЄҐ")]
    [InlineData(" Café ", "CAFÉ")]
    [InlineData("Σςσ", "ΣΣΣ")]
    [InlineData("İıi", "İII")]
    [InlineData("ßẞ", "ßẞ")]
    [InlineData("𐐨😀", "𐐀😀")]
    [InlineData("𐐨a😀b", "𐐀A😀B")]
    public void NamesUseSpecifiedSimpleUppercase(string input, string expected) => Assert.Equal(expected, NameIdentity.Key(input));

    [Fact]
    public void ServiceMatchingUsesCanonicalKeysAndDuplicateInputIsAtomic()
    {
        var room = new Room("Room", 1, 1000m);
        room.SetServices([new("їєґ", 300m)]);
        var id = room.Services.Single().Id;
        room.SetServices([new("\u00a0ЇЄҐ\u2009", 500m)]);
        Assert.Equal(id, room.Services.Single().Id);
        Assert.Equal("їєґ", room.Services.Single().Name);
        Assert.Throws<ArgumentException>(() => room.SetServices([new("ЇЄҐ", 600m), new(" їєґ ", 700m)]));
        Assert.Equal(500m, room.Services.Single().Price);
        Assert.NotEqual(NameIdentity.Key("A"), NameIdentity.Key("А"));
        Assert.NotEqual(NameIdentity.Key("é"), NameIdentity.Key("e\u0301"));
        Assert.NotEqual(NameIdentity.Key("two spaces"), NameIdentity.Key("two  spaces"));
    }

    [Fact]
    public void FineTimeInputIsRejectedBeforeCalculationAndClockIsFloored()
    {
        var room = new Room("Room", 1, 1000m);
        Assert.Throws<BookingValidationException>(() => room.Book(At(10).AddTicks(1), At(11), At(9), [], InitialRules()));
        Assert.Throws<BookingValidationException>(() => BookingPriceCalculator.Calculate(At(10), At(11).AddTicks(1), 1000m, InitialRules()));
        Assert.Throws<ArgumentException>(() => new BookingPricingRule("Fine", "Fine", new TimeOnly(10, 0).Add(TimeSpan.FromTicks(1)), new TimeOnly(11, 0), 1m, 0));
        var booking = room.Book(At(10), At(11), At(10).AddTicks(9), [], InitialRules());
        Assert.Equal(At(10), booking.CreatedAtUtc);
        Assert.Equal(new DateTime(DateTime.MaxValue.Ticks - 9, DateTimeKind.Utc), UtcPrecision.Floor(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)));
    }
}
