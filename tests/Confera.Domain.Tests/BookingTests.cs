using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using static Confera.Domain.Tests.BookingTestData;

namespace Confera.Domain.Tests;

public sealed class BookingTests
{
    [Fact]
    public void Book_CreatesCompleteBookingAndOwnsEverySnapshot()
    {
        var room = CreateRoom();
        var now = At(10);
        var booking = room.Book(At(11), At(15), now, room.Services.Select(x => x.Id).ToArray(), InitialRules());

        Assert.NotEqual(Guid.Empty, booking.Id);
        Assert.Equal(room.Id, booking.RoomId);
        Assert.Equal(At(11), booking.StartsAtUtc);
        Assert.Equal(At(15), booking.EndsAtUtc);
        Assert.Equal(now, booking.CreatedAtUtc);
        Assert.Equal(2000m, booking.HourlyRateSnapshot);
        Assert.Equal(9400m, booking.TotalPrice);
        Assert.Equal(2, booking.Services.Count);
        Assert.Equal(3, booking.PriceSegments.Count);
        Assert.All(booking.Services, x => Assert.Equal(booking.Id, x.BookingId));
        Assert.All(booking.PriceSegments, x => Assert.Equal(booking.Id, x.BookingId));
        var childIds = booking.Services.Select(x => x.Id).Concat(booking.PriceSegments.Select(x => x.Id)).ToArray();
        Assert.DoesNotContain(Guid.Empty, childIds);
        Assert.Equal(childIds.Length, childIds.Distinct().Count());
        Assert.Equal(booking.TotalPrice,
            booking.PriceSegments.Sum(x => x.Price) + booking.Services.Sum(x => x.ServicePriceSnapshot));
    }

    [Fact]
    public void Book_ChargesSelectedServiceOnceWithoutTariffMultiplier()
    {
        var room = CreateRoom();
        var projector = room.Services.Single(x => x.Name == "Projector");

        var booking = room.Book(At(12), At(14), At(11), [projector.Id], InitialRules());

        Assert.Equal(5100m, booking.TotalPrice);
        Assert.Equal(500m, Assert.Single(booking.Services).ServicePriceSnapshot);
    }

    [Fact]
    public void Book_AllowsEmptyServiceSelection()
    {
        var booking = CreateRoom().Book(At(11), At(15), At(10), [], InitialRules());

        Assert.Empty(booking.Services);
        Assert.Equal(8600m, booking.TotalPrice);
    }

    [Fact]
    public void Book_RejectsDuplicateServiceIds()
    {
        var room = CreateRoom();
        var id = room.Services.First().Id;

        var error = Assert.Throws<BookingValidationException>(() => room.Book(At(11), At(15), At(10), [id, id], InitialRules()));
        Assert.Equal(BookingValidationError.InvalidServiceSelection, error.Error);
    }

    [Fact]
    public void Book_RejectsAnotherRoomsService()
    {
        var room = CreateRoom();
        var anotherRoomsService = CreateRoom().Services.First().Id;

        var error = Assert.Throws<BookingValidationException>(() =>
            room.Book(At(11), At(15), At(10), [anotherRoomsService], InitialRules()));
        Assert.Equal(BookingValidationError.InvalidServiceSelection, error.Error);
    }

    [Fact]
    public void Book_PreservesSnapshotsAfterRoomAndServicesChange()
    {
        var room = CreateRoom();
        var booking = room.Book(At(11), At(15), At(10), room.Services.Select(x => x.Id).ToArray(), InitialRules());

        room.SetHourlyRate(3000m);
        room.SetServices([new("Projector", 900m), new("Sound", 700m)]);
        var newBooking = room.Book(At(11), At(15), At(10), room.Services.Select(x => x.Id).ToArray(),
            [Rule("new", 9, 18, 2m)]);

        Assert.Equal(2000m, booking.HourlyRateSnapshot);
        Assert.Equal(9400m, booking.TotalPrice);
        Assert.Equal(new[] { "Projector", "Wi-Fi" }, booking.Services.Select(x => x.ServiceNameSnapshot));
        Assert.Equal(new[] { 500m, 300m }, booking.Services.Select(x => x.ServicePriceSnapshot));
        Assert.All(booking.PriceSegments, x => Assert.Equal(2000m, x.HourlyRateSnapshot));
        Assert.Contains(booking.PriceSegments, x => x.PricingCodeSnapshot == "peak" && x.MultiplierSnapshot == 1.15m);
        Assert.Equal(25600m, newBooking.TotalPrice);
        Assert.NotEqual(booking.Id, newBooking.Id);
    }

    [Fact]
    public void Book_AllowsZeroShortSegmentAfterRounding()
    {
        var room = new Room("Small segment", 1, 1000m);

        var booking = room.Book(At(9), At(9, 30), At(9), [],
            [Rule("day", 9, 18), new("edge", "Edge", new TimeOnly(9, 0), new TimeOnly(9, 0).Add(TimeSpan.FromTicks(10)), 0.5m, 20)]);

        Assert.Equal(500m, booking.TotalPrice);
        Assert.Equal(0m, booking.PriceSegments.First().Price);
    }

    [Fact]
    public void Book_AllowsShortSegmentsWithinValidBooking()
    {
        var room = new Room("Room", 1, 1800m);
        BookingPricingRule[] rules =
        [
            new("first", "First", new TimeOnly(9, 0), new TimeOnly(9, 29), 1m, 0),
            new("second", "Second", new TimeOnly(9, 29), new TimeOnly(10, 0), 1m, 1)
        ];

        var booking = room.Book(At(9), At(9, 30), At(9), [], rules);

        Assert.Equal(new[] { 870m, 30m }, booking.PriceSegments.Select(x => x.Price));
        Assert.Equal(900m, booking.TotalPrice);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(37)]
    [InlineData(1440)]
    public void Book_AcceptsDurationBoundsAndNonMultipleOf30(int minutes)
    {
        var booking = CreateRoom().Book(At(10), At(10).AddMinutes(minutes), At(10), [],
            [Rule("day", 9, 18), Rule("night", 18, 9)]);

        Assert.Equal(TimeSpan.FromMinutes(minutes), booking.EndsAtUtc - booking.StartsAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(29)]
    [InlineData(1441)]
    public void Book_RejectsInvalidDuration(int minutes)
    {
        Assert.ThrowsAny<ArgumentException>(() => CreateRoom().Book(At(10), At(10).AddMinutes(minutes),
            At(9), [], InitialRules()));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Book_RejectsDurationJustOutsideBounds(int boundary)
    {
        var duration = boundary < 0
            ? TimeSpan.FromMinutes(30).Subtract(TimeSpan.FromTicks(10))
            : TimeSpan.FromHours(24).Add(TimeSpan.FromTicks(10));

        Assert.Throws<BookingValidationException>(() => CreateRoom().Book(At(10), At(10).Add(duration),
            At(9), [], InitialRules()));
    }

    [Fact]
    public void Book_RejectsPastStart()
    {
        var error = Assert.Throws<BookingValidationException>(() => CreateRoom().Book(At(10), At(11), At(10).AddTicks(10), [], InitialRules()));
        Assert.Equal(BookingValidationError.InvalidPeriod, error.Error);
    }

    [Theory]
    [InlineData(DateTimeKind.Local, 0)]
    [InlineData(DateTimeKind.Unspecified, 0)]
    [InlineData(DateTimeKind.Local, 1)]
    [InlineData(DateTimeKind.Unspecified, 1)]
    [InlineData(DateTimeKind.Local, 2)]
    [InlineData(DateTimeKind.Unspecified, 2)]
    public void Book_RejectsNonUtcTimes(DateTimeKind kind, int changedTime)
    {
        var start = At(10);
        var end = At(11);
        var now = At(9);
        if (changedTime == 0)
        {
            start = DateTime.SpecifyKind(start, kind);
        }

        if (changedTime == 1)
        {
            end = DateTime.SpecifyKind(end, kind);
        }

        if (changedTime == 2)
        {
            now = DateTime.SpecifyKind(now, kind);
        }

        if (changedTime == 2)
        {
            Assert.Throws<ArgumentException>(() => CreateRoom().Book(start, end, now, [], InitialRules()));
        }
        else
        {
            Assert.Throws<BookingValidationException>(() => CreateRoom().Book(start, end, now, [], InitialRules()));
        }
    }

    [Fact]
    public void Book_RejectsNullSelectionsAndRules()
    {
        var room = CreateRoom();

        Assert.Throws<BookingValidationException>(() => room.Book(At(10), At(11), At(9), null!, InitialRules()));
        Assert.Throws<ArgumentNullException>(() => room.Book(At(10), At(11), At(9), [], null!));
    }

    [Fact]
    public void Book_DoesNotExposeMutableCollections()
    {
        var room = CreateRoom();
        var booking = room.Book(At(11), At(15), At(10), room.Services.Select(x => x.Id).ToArray(), InitialRules());

        Assert.Throws<NotSupportedException>(() => ((ICollection<RoomService>)room.Services).Clear());
        Assert.Throws<NotSupportedException>(() => ((ICollection<BookedRoomServiceSnapshot>)booking.Services).Clear());
        Assert.Throws<NotSupportedException>(() => ((ICollection<BookingPriceSegment>)booking.PriceSegments).Clear());
        Assert.Equal(9400m, booking.TotalPrice);
    }

    private static Room CreateRoom()
    {
        var room = new Room("Room A", 50, 2000m);
        room.SetServices([new("Projector", 500m), new("Wi-Fi", 300m)]);
        return room;
    }
}
