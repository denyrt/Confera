using Confera.Application.Availability;
using Confera.Application.Rooms;
using Confera.Domain.Bookings;

namespace Confera.Application.Tests;

public sealed class SearchAvailabilityTests
{
    private static DateTime At(int hour) => new(2030, 1, 15, hour, 0, 0, DateTimeKind.Utc);
    private static AvailabilityQuery Query() => new(At(11), At(15), 50);

    [Theory]
    [InlineData("capacity")]
    [InlineData("page")]
    [InlineData("page_size_zero")]
    [InlineData("page_size_large")]
    [InlineData("overflow")]
    [InlineData("past")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("precision")]
    [InlineData("utc")]
    public async Task InvalidInputDoesNotReadDatabase(string scenario)
    {
        var reader = new TestReader();
        var query = scenario switch
        {
            "capacity" => Query() with { Capacity = 0 },
            "page" => Query() with { Page = 0 },
            "page_size_zero" => Query() with { PageSize = 0 },
            "page_size_large" => Query() with { PageSize = 101 },
            "overflow" => Query() with { Page = int.MaxValue },
            "past" => Query() with { StartsAtUtc = At(8) },
            "short" => Query() with { EndsAtUtc = At(11).AddMinutes(29) },
            "long" => Query() with { EndsAtUtc = At(11).AddDays(1).AddTicks(10) },
            "precision" => Query() with { EndsAtUtc = At(15).AddTicks(1) },
            _ => Query() with { StartsAtUtc = DateTime.SpecifyKind(At(11), DateTimeKind.Unspecified) }
        };

        var error = await Record.ExceptionAsync(() => Service(reader).SearchAsync(query, TestContext.Current.CancellationToken));
        Assert.True(error is AvailabilityOperationException { Failure: AvailabilityFailure.InvalidRequest }
            or BookingValidationException { Error: BookingValidationError.InvalidPeriod });
        Assert.Equal(0, reader.TariffReads);
        Assert.Equal(0, reader.RoomReads);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public async Task ReturnsRequestedPageAndUsesOneExtraCandidate(int count, bool hasNextPage)
    {
        var reader = new TestReader
        {
            Rooms = Enumerable.Range(0, count)
            .Select(i => new RoomResult(Guid.NewGuid(), $"Room {i}", 50, 2000m, "UAH", [])).ToArray()
        };
        var clock = new TestClock(At(11).AddTicks(9));
        var page = await new SearchAvailabilityService(reader, clock).SearchAsync(Query() with { Page = 3, PageSize = 2 },
            TestContext.Current.CancellationToken);

        Assert.Equal(reader.Rooms.Take(2), page.Items);
        Assert.Equal((3, 2, hasNextPage), (page.Page, page.PageSize, page.HasNextPage));
        Assert.Equal((50, 4, 3), reader.Request);
        Assert.Equal(RentalPeriod.Create(At(11), At(15)), reader.Period);
        Assert.Equal(1, clock.Reads);
    }

    [Fact]
    public async Task MissingCoverageSkipsRoomReadAndPreservesPage()
    {
        var reader = new TestReader { Rules = [] };
        var page = await Service(reader).SearchAsync(Query() with { Page = 7 }, TestContext.Current.CancellationToken);
        Assert.Empty(page.Items);
        Assert.Equal((7, 20, false), (page.Page, page.PageSize, page.HasNextPage));
        Assert.Equal(1, reader.TariffReads);
        Assert.Equal(0, reader.RoomReads);
    }

    [Fact]
    public async Task InvalidConfigurationIsNotAnEmptySearch()
    {
        var reader = new TestReader();
        reader.Rules = [reader.Rules[0], reader.Rules[0]];
        await Assert.ThrowsAsync<ArgumentException>(() => Service(reader).SearchAsync(Query(), TestContext.Current.CancellationToken));
        Assert.Equal(0, reader.RoomReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAtEitherReadPropagatesWithoutRetry(bool failRooms)
    {
        var reader = new TestReader { FailTariffs = !failRooms, FailRooms = failRooms };
        var error = await Assert.ThrowsAsync<AvailabilityOperationException>(() =>
            Service(reader).SearchAsync(Query(), TestContext.Current.CancellationToken));
        Assert.Equal(AvailabilityFailure.PersistenceUnavailable, error.Failure);
        Assert.Equal(1, reader.TariffReads);
        Assert.Equal(failRooms ? 1 : 0, reader.RoomReads);
    }

    [Fact]
    public async Task CancellationIsPassedToBothReadsAndAlreadyCanceledSearchDoesNotRead()
    {
        var reader = new TestReader();
        using var source = new CancellationTokenSource();
        await Service(reader).SearchAsync(Query(), source.Token);
        Assert.Equal(new[] { source.Token, source.Token }, reader.Tokens);
        await source.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Service(reader).SearchAsync(Query(), source.Token));
        Assert.Equal(1, reader.TariffReads);
        Assert.Equal(1, reader.RoomReads);
    }

    private static SearchAvailabilityService Service(TestReader reader) => new(reader, new TestClock(At(9)));

    private sealed class TestClock(DateTime now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return new DateTimeOffset(now);
        }
    }

    private sealed class TestReader : IAvailabilityReader
    {
        public IReadOnlyList<BookingPricingRule> Rules { get; set; } =
            [new("day", "Day", new TimeOnly(9, 0), new TimeOnly(18, 0), 1m, 0)];
        public IReadOnlyList<RoomResult> Rooms { get; set; } = [];
        public int TariffReads { get; private set; }
        public int RoomReads { get; private set; }
        public bool FailTariffs { get; init; }
        public bool FailRooms { get; init; }
        public (int Capacity, int Offset, int Limit) Request { get; private set; }
        public RentalPeriod? Period { get; private set; }
        public List<CancellationToken> Tokens { get; } = [];

        public Task<IReadOnlyList<BookingPricingRule>> GetPricingRulesAsync(CancellationToken cancellationToken)
        {
            TariffReads++;
            Tokens.Add(cancellationToken);
            if (FailTariffs)
            {
                throw new AvailabilityOperationException(AvailabilityFailure.PersistenceUnavailable);
            }

            return Task.FromResult(Rules);
        }

        public Task<IReadOnlyList<RoomResult>> GetAvailableRoomsAsync(RentalPeriod period, int capacity,
            int offset, int limit, CancellationToken cancellationToken)
        {
            RoomReads++;
            Tokens.Add(cancellationToken);
            Period = period;
            Request = (capacity, offset, limit);
            if (FailRooms)
            {
                throw new AvailabilityOperationException(AvailabilityFailure.PersistenceUnavailable);
            }

            return Task.FromResult(Rooms);
        }
    }
}
