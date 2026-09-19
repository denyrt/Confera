using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using static Confera.Domain.Tests.BookingTestData;

namespace Confera.Domain.Tests;

public sealed class BookingValidationTests
{
    [Theory]
    [InlineData("equal", false)]
    [InlineData("reversed", false)]
    [InlineData("local_start", false)]
    [InlineData("unspecified_end", false)]
    [InlineData("start_precision", false)]
    [InlineData("end_precision_and_duration", false)]
    [InlineData("too_short", true)]
    [InlineData("too_long", true)]
    public void TryCreateRejectsInvalidPeriodsAndThrowingWrapperAgrees(string scenario, bool durationFailure)
    {
        var start = At(10);
        var end = At(11);
        switch (scenario)
        {
            case "equal": end = start; break;
            case "reversed": end = At(9); break;
            case "local_start": start = DateTime.SpecifyKind(start, DateTimeKind.Local); break;
            case "unspecified_end": end = DateTime.SpecifyKind(end, DateTimeKind.Unspecified); break;
            case "start_precision": start = start.AddTicks(1); break;
            case "end_precision_and_duration": end = start.AddMinutes(30).AddTicks(-1); break;
            case "too_short": end = start.AddMinutes(30).AddTicks(-10); break;
            case "too_long": end = start.AddHours(24).AddTicks(10); break;
        }

        Assert.False(RentalPeriod.TryCreate(start, end, out var period, out var failure));
        Assert.Null(period);
        Assert.NotNull(failure);
        Assert.Equal(BookingValidationError.InvalidPeriod, failure.Kind);
        Assert.Equal(durationFailure
            ? "Booking duration must be between 30 minutes and 24 hours."
            : "The period must have ordered UTC endpoints with whole-microsecond precision.", failure.Description);
        var exception = Assert.Throws<BookingValidationException>(() => RentalPeriod.Create(start, end));
        Assert.Equal(failure.Kind, exception.Error);
        Assert.Equal(failure.Description, exception.Message);
        Assert.Equal(failure.Description,
            Assert.Throws<BookingValidationException>(() => BookingValidation.RequireRentalPeriod(start, end)).Message);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(37)]
    [InlineData(1440)]
    public void TryCreateAcceptsMicrosecondEndpointsAndDurationBounds(int minutes)
    {
        var start = At(10).AddTicks(10);
        Assert.True(RentalPeriod.TryCreate(start, start.AddMinutes(minutes), out var period, out var failure));
        Assert.NotNull(period);
        Assert.Null(failure);
        Assert.Equal(start, period.StartsAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(minutes), period.Duration);
        Assert.Equal(RentalPeriod.Create(start, start.AddMinutes(minutes)), period);
    }

    [Theory]
    [InlineData("null", BookingValidationError.InvalidServiceSelection)]
    [InlineData("empty_id", BookingValidationError.InvalidServiceSelection)]
    [InlineData("duplicate", BookingValidationError.InvalidServiceSelection)]
    [InlineData("unknown", BookingValidationError.InvalidServiceSelection)]
    [InlineData("coverage", BookingValidationError.MissingTariffCoverage)]
    [InlineData("past", BookingValidationError.InvalidPeriod)]
    public void TryBookRejectsWithoutCreatingBookingOrChangingRoom(string scenario, BookingValidationError kind)
    {
        var room = new Room("Room", 50, 2000m);
        room.SetServices([new("Projector", 500m)]);
        var services = room.Services.ToArray();
        var version = room.Version;
        var id = services[0].Id;
        Guid[]? selection = scenario switch
        {
            "null" => null,
            "empty_id" => [Guid.Empty],
            "duplicate" => [id, id],
            "unknown" or "past" => [Guid.NewGuid()],
            _ => []
        };
        var period = Period(At(11), At(15));
        var now = scenario == "past" ? At(12) : At(10);

        // No rules also exercise precedence: period and selection errors must win over coverage.
        Assert.False(room.TryBook(period, now, selection, [], out var booking, out var failure));
        Assert.Null(booking);
        Assert.NotNull(failure);
        Assert.Equal(kind, failure.Kind);
        var exception = Assert.Throws<BookingValidationException>(() => room.Book(period, now, selection!, []));
        Assert.Equal(failure.Description, exception.Message);
        Assert.Equal(version, room.Version);
        Assert.Equal(services, room.Services);
    }

    [Fact]
    public void TryMethodsKeepClockConfigurationAndLifecycleDefectsExceptional()
    {
        var room = new Room("Room", 50, 2000m);
        var period = Period(At(11), At(15));
        Assert.Throws<ArgumentException>(() => BookingValidation.TryValidateBookingPeriod(period, At(10).AddTicks(1), out _));
        Assert.Throws<ArgumentException>(() => room.TryBook(period, DateTime.SpecifyKind(At(10), DateTimeKind.Unspecified), [], [], out _, out _));
        Assert.Throws<ArgumentNullException>(() => room.TryBook(period, At(10), [], null!, out _, out _));
        var rule = InitialRules()[0];
        Assert.Throws<ArgumentException>(() => room.TryBook(period, At(10), [], [rule, rule], out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => BookingPriceCalculator.TryCalculate(period, 999m, [], out _, out _));
        room.Delete();
        Assert.Throws<InvalidOperationException>(() => room.TryBook(period, At(10), [], [], out _, out _));
    }
}
