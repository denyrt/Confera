using Confera.Application.Reports;

namespace Confera.Application.Tests;

public sealed class ReportServiceTests
{
    private static DateTime At(int day) => new(2030, 1, day, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("equal")]
    [InlineData("reversed")]
    [InlineData("start_kind")]
    [InlineData("end_kind")]
    [InlineData("start_precision")]
    [InlineData("end_precision")]
    public async Task InvalidPeriodRejectsBothReportsBeforeReading(string scenario)
    {
        var start = scenario switch
        {
            "start_kind" => DateTime.SpecifyKind(At(1), DateTimeKind.Unspecified),
            "start_precision" => At(1).AddTicks(1),
            _ => At(1)
        };
        var end = scenario switch
        {
            "equal" => start,
            "reversed" => start.AddTicks(-10),
            "end_kind" => DateTime.SpecifyKind(At(2), DateTimeKind.Local),
            "end_precision" => At(2).AddTicks(1),
            _ => At(2)
        };
        var reader = new TestReader();
        var service = new ReportService(reader);

        var roomError = await Assert.ThrowsAsync<ReportOperationException>(() =>
            service.GetRoomsAsync(start, end, TestContext.Current.CancellationToken));
        var serviceError = await Assert.ThrowsAsync<ReportOperationException>(() =>
            service.GetServicesAsync(start, end, TestContext.Current.CancellationToken));

        Assert.Equal(ReportFailure.InvalidPeriod, roomError.Failure);
        Assert.Equal(ReportFailure.InvalidPeriod, serviceError.Failure);
        Assert.Empty(reader.Reads);
    }

    [Theory]
    [InlineData(2000, 10L)]
    [InlineData(2040, TimeSpan.TicksPerDay * 365L)]
    public async Task ReportsAcceptPastFutureAndPeriodsOutsideBookingBounds(int year, long ticks)
    {
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddTicks(ticks);
        var reader = new TestReader();
        var service = new ReportService(reader);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var rooms = await service.GetRoomsAsync(start, end, cancellation.Token);
        var services = await service.GetServicesAsync(start, end, cancellation.Token);

        Assert.Equal((start, end, "UAH"), (rooms.Start, rooms.End, rooms.Currency));
        Assert.Equal((start, end, "UAH"), (services.Start, services.End, services.Currency));
        Assert.Empty(rooms.Items);
        Assert.Empty(services.Items);
        Assert.Equal(new[] { "rooms", "services" }, reader.Reads);
        Assert.Equal(ReportPeriod.Create(start, end), reader.Period);
        Assert.Equal(cancellation.Token, reader.Token);
    }

    [Fact]
    public async Task AlreadyCanceledReportsDoNotRead()
    {
        var reader = new TestReader();
        var service = new ReportService(reader);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetRoomsAsync(At(1), At(2), cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetServicesAsync(At(1), At(2), cancellation.Token));
        Assert.Empty(reader.Reads);
    }

    private sealed class TestReader : IReportReader
    {
        public List<string> Reads { get; } = [];
        public ReportPeriod? Period { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<IReadOnlyList<RoomReportRow>> GetRoomsAsync(ReportPeriod period, CancellationToken cancellationToken)
        {
            Reads.Add("rooms");
            Period = period;
            Token = cancellationToken;
            return Task.FromResult<IReadOnlyList<RoomReportRow>>([]);
        }

        public Task<IReadOnlyList<ServiceReportRow>> GetServicesAsync(ReportPeriod period, CancellationToken cancellationToken)
        {
            Reads.Add("services");
            Period = period;
            Token = cancellationToken;
            return Task.FromResult<IReadOnlyList<ServiceReportRow>>([]);
        }
    }
}
