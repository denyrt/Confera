using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Confera.Application.Availability;
using Confera.Application.Bookings;
using Confera.Application.Rooms;
using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using Confera.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using static Confera.Integration.Tests.BookingHttpTests;
using static Confera.Integration.Tests.BookingTestSupport;

namespace Confera.Integration.Tests;

public sealed class AvailabilityTests(PostgresFixture postgres)
{
    [Fact]
    public async Task FiltersAndPagesRoomsInDatabaseWithAllCurrentServices()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, other) = await SeedAsync(database);
        var sameCapacity = new Room("Same capacity", 50, 3000m);
        var noServices = new Room("No services", 60, 1000m);
        var tooSmall = new Room("Small", 49, 1000m);
        var deleted = new Room("Deleted", 50, 1000m);
        deleted.Delete();
        var occupied = new Room("Occupied", 50, 1000m);
        await using (var db = database.Context())
        {
            db.AddRange(sameCapacity, noServices, tooSmall, deleted, occupied);
            db.Add(occupied.Book(Period(At(12), At(13)), At(9), [], Rules()));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var observer = new SearchReadObserver();
        var service = Search(database, observer);
        var expected = new[] { room, sameCapacity, noServices, other }.OrderBy(x => x.Capacity).ThenBy(x => x.Id).ToArray();
        var collected = new List<Guid>();
        for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            observer.Commands.Clear();
            var page = await service.SearchAsync(new(At(11), At(15), 50, pageNumber, 2), TestContext.Current.CancellationToken);
            Assert.Equal(expected.Skip((pageNumber - 1) * 2).Take(2).Select(x => x.Id), page.Items.Select(x => x.Id));
            Assert.Equal(pageNumber == 1, page.HasNextPage);
            collected.AddRange(page.Items.Select(x => x.Id));
            foreach (var result in page.Items)
            {
                var source = expected.Single(x => x.Id == result.Id);
                Assert.Equal(source.Services.OrderBy(x => x.Id).Select(x => (x.Id, x.Name, x.Price)),
                    result.Services.Select(x => (x.Id, x.Name, x.Price)));
                Assert.Equal(source.HourlyRate, result.HourlyRate);
                Assert.Equal("UAH", result.Currency);
            }

            Assert.Equal(2, observer.Commands.Count);
            var roomQuery = observer.Commands[1];
            Assert.Contains("LIMIT", roomQuery);
            Assert.Contains("OFFSET", roomQuery);
            Assert.Contains("EXISTS", roomQuery);
            Assert.Contains("RoomServices", roomQuery);
            Assert.DoesNotContain("count(", roomQuery, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TotalPrice", roomQuery);
            Assert.DoesNotContain("BookingPriceSegments", roomQuery);
            Assert.DoesNotContain("FOR UPDATE", roomQuery);
        }

        Assert.Equal(expected.Select(x => x.Id), collected);
        var largeCapacity = await service.SearchAsync(new(At(11), At(15), int.MaxValue), TestContext.Current.CancellationToken);
        Assert.Empty(largeCapacity.Items);
    }

    [Theory]
    [InlineData(9, 11, true)]
    [InlineData(15, 17, true)]
    [InlineData(10, 12, false)]
    [InlineData(14, 16, false)]
    [InlineData(12, 14, false)]
    [InlineData(10, 16, false)]
    [InlineData(11, 15, false)]
    public async Task HalfOpenOverlapMatchesBookingRules(int start, int end, bool available)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, other) = await SeedAsync(database);
        await using (var db = database.Context())
        {
            db.Add(room.Book(Period(At(start), At(end)), At(9), [], Rules()));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var page = await Search(database).SearchAsync(new(At(11), At(15), 50), TestContext.Current.CancellationToken);
        Assert.Equal(available, page.Items.Any(x => x.Id == room.Id));
        Assert.Contains(page.Items, x => x.Id == other.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingCoverageReadsOnlyTariffs(bool emptyRules)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await SeedAsync(database);
        if (emptyRules)
        {
            await using var db = database.Context();
            await db.PricingRules.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        var observer = new SearchReadObserver();
        var page = await Search(database, observer).SearchAsync(new(At(22), At(0).AddDays(1), 50, 4),
            TestContext.Current.CancellationToken);
        Assert.Empty(page.Items);
        Assert.Equal((4, 20, false), (page.Page, page.PageSize, page.HasNextPage));
        Assert.Single(observer.Commands);
    }

    [Theory]
    [InlineData("2030-01-15T11:00:00Z", "2030-01-15T15:00:00Z")]
    [InlineData("2030-01-15T13:00:00+02:00", "2030-01-15T17:00:00+02:00")]
    [InlineData("2030-01-15T11:00Z", "2030-01-15T15:00Z")]
    [InlineData("2030-01-15T11:00:00.1234560000000000Z", "2030-01-15T15:00:00.123456Z")]
    public async Task HttpSearchAcceptsSharedTimestampFormatsAndDefaults(string start, string end)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, other) = await SeedAsync(database);
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync(Url(start, end), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var page = (await response.Content.ReadFromJsonAsync<AvailabilityPage>(TestContext.Current.CancellationToken))!;
        Assert.Equal((1, 20, false), (page.Page, page.PageSize, page.HasNextPage));
        Assert.Equal(new[] { room.Id, other.Id }, page.Items.Select(x => x.Id));
        Assert.Equal(2, page.Items[0].Services.Count);
    }

    [Fact]
    public async Task HttpRejectsMalformedQueryAndPeriodBeforeDatabaseReads()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var observer = new SearchReadObserver();
        await using var factory = new BookingApiFactory(database, observer);
        using var client = factory.CreateHttpsClient();
        foreach (var field in new[] { "start", "end", "capacity" })
        {
            var query = Query();
            query.Remove(field);
            await Reject(query, "invalid_request");
        }

        foreach (var (field, value) in new[]
        {
            ("start", ""), ("start", "2030-01-15T11:00:00"), ("start", "2030-01-15"),
            ("start", "2030-01-15T11:00:00.0000001Z"), ("start", "2030-01-15T11:00:00.0000000000000001Z"),
            ("start", "2030-01-15T11:00:00.00000000000000000Z"), ("end", "2030-02-30T15:00:00Z"),
            ("start", "2030-01-15T11:00:00+99:00"), ("start", "2030-01-15 11:00:00Z"),
            ("capacity", "0"), ("capacity", "-1"), ("capacity", "1.5"), ("capacity", "2147483648"),
            ("page", "0"), ("page", "-1"), ("page", "2147483647"), ("page", "text"), ("page", ""),
            ("pageSize", "0"), ("pageSize", "101"), ("pageSize", "")
        })
        {
            var query = Query();
            query[field] = value;
            await Reject(query, "invalid_request");
        }

        foreach (var (start, end) in new[]
        {
            ("2030-01-15T08:00:00Z", "2030-01-15T15:00:00Z"),
            ("2030-01-15T11:00:00Z", "2030-01-15T10:00:00Z"),
            ("2030-01-15T11:00:00Z", "2030-01-15T11:29:59.999999Z"),
            ("2030-01-15T11:00:00Z", "2030-01-16T11:00:00.000001Z")
        })
        {
            var query = Query();
            query["start"] = start;
            query["end"] = end;
            await Reject(query, "invalid_booking_period");
        }

        Assert.Empty(observer.Commands);

        async Task Reject(Dictionary<string, string?> query, string code)
        {
            using var response = await client.GetAsync(QueryHelpers.AddQueryString("/rooms/availability", query),
                TestContext.Current.CancellationToken);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
        }
    }

    [Fact]
    public async Task HttpPagesAndUncoveredPeriodReturnSuccessfulEnvelopes()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, other) = await SeedAsync(database);
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        for (var number = 1; number <= 3; number++)
        {
            var page = (await client.GetFromJsonAsync<AvailabilityPage>(Url(page: number, pageSize: 1), TestContext.Current.CancellationToken))!;
            Assert.Equal((number, 1, number == 1), (page.Page, page.PageSize, page.HasNextPage));
            Assert.Equal(new[] { room.Id, other.Id }.Skip(number - 1).Take(1), page.Items.Select(x => x.Id));
        }

        var uncovered = (await client.GetFromJsonAsync<AvailabilityPage>(Url("2030-01-15T22:00:00Z", "2030-01-16T00:00:00Z"),
            TestContext.Current.CancellationToken))!;
        Assert.Empty(uncovered.Items);
        Assert.False(uncovered.HasNextPage);
        var shortBooking = (await client.GetFromJsonAsync<AvailabilityPage>(Url(end: "2030-01-15T11:30:00Z", pageSize: 100),
            TestContext.Current.CancellationToken))!;
        Assert.Equal(2, shortBooking.Items.Count);
    }

    [Fact]
    public async Task CurrentR1OfferingsCanBeBookedButSearchDoesNotReserve()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        var before = (await client.GetFromJsonAsync<AvailabilityPage>(Url(), TestContext.Current.CancellationToken))!.Items[0];
        var oldWifi = before.Services.Single(x => x.Name == "Wi-Fi").Id;
        var management = new RoomManagementService(new RoomStore(new BookingContextFactory(database)), new BookingClock(At(9)));
        await management.UpdateAsync(room.Id, new RoomCommand("Room A", 50, 2500m,
            [new("Projector", 600m), new("Sound", 700m)]), [room.Version], TestContext.Current.CancellationToken);

        var current = (await client.GetFromJsonAsync<AvailabilityPage>(Url(), TestContext.Current.CancellationToken))!.Items[0];
        Assert.Equal(2500m, current.HourlyRate);
        Assert.DoesNotContain(current.Services, x => x.Id == oldWifi);
        Assert.Equal(before.Services.Single(x => x.Name == "Projector").Id, current.Services.Single(x => x.Name == "Projector").Id);
        Assert.Equal(600m, current.Services.Single(x => x.Name == "Projector").Price);
        using var roomRead = await client.GetAsync($"/rooms/{room.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, roomRead.StatusCode);
        Assert.NotNull(roomRead.Headers.ETag);

        var payload = new { roomId = current.Id, start = At(11), end = At(15), serviceIds = current.Services.Select(x => x.Id).ToArray() };
        using var otherClient = factory.CreateHttpsClient();
        using var booked = await otherClient.PostAsJsonAsync("/bookings", payload, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        var booking = (await booked.Content.ReadFromJsonAsync<BookingResult>(TestContext.Current.CancellationToken))!;
        Assert.Equal(12050m, booking.TotalPrice);
        using var conflict = await client.PostAsJsonAsync("/bookings", payload, TestContext.Current.CancellationToken);
        await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "room_unavailable");
        var after = (await client.GetFromJsonAsync<AvailabilityPage>(Url(), TestContext.Current.CancellationToken))!;
        Assert.DoesNotContain(after.Items, x => x.Id == room.Id);
    }

    [Theory]
    [InlineData(false, "timeout", 503, "availability_persistence_unavailable")]
    [InlineData(true, "timeout", 503, "availability_persistence_unavailable")]
    [InlineData(false, "schema", 500, "internal_error")]
    [InlineData(true, "argument", 500, "internal_error")]
    public async Task HttpFailuresStaySafeAndAreNotRetried(bool failRooms, string failure, int status, string code)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await SeedAsync(database);
        var observer = new SearchReadObserver(failRooms, failure);
        await using var factory = new BookingApiFactory(database, observer);
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync(Url(), TestContext.Current.CancellationToken);
        await AssertProblemAsync(response, (HttpStatusCode)status, code);
        Assert.Equal(failRooms ? 2 : 1, observer.Commands.Count);
    }

    [Fact]
    public async Task DatabaseReadHonorsCancellation()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await SeedAsync(database);
        var observer = new SearchReadObserver(pause: true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var search = Search(database, observer).SearchAsync(new(At(11), At(15), 50), cancellation.Token);
        await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search);
        Assert.Single(observer.Commands);
        Assert.NotEmpty((await Search(database).SearchAsync(new(At(11), At(15), 50), TestContext.Current.CancellationToken)).Items);
    }

    [Fact]
    public async Task OpenApiDescribesSearchParametersPaginationAndResponses()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));
        var operation = document.RootElement.GetProperty("paths").GetProperty("/rooms/availability").GetProperty("get");
        foreach (var status in new[] { "200", "400", "500", "503" })
        {
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        }
        var parameters = operation.GetProperty("parameters").EnumerateArray()
            .ToDictionary(x => x.GetProperty("name").GetString()!, StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "start", "end", "capacity" })
        {
            Assert.True(parameters[name].TryGetProperty("required", out var required) && required.GetBoolean(), operation.GetRawText());
        }
        Assert.Equal(1, parameters["page"].GetProperty("schema").GetProperty("default").GetInt32());
        Assert.Equal(20, parameters["pageSize"].GetProperty("schema").GetProperty("default").GetInt32());
        Assert.Equal(100, parameters["pageSize"].GetProperty("schema").GetProperty("maximum").GetInt32());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var fields = schemas.GetProperty("AvailabilityPage").GetProperty("properties");
        Assert.Equal(new[] { "hasNextPage", "items", "page", "pageSize" }, fields.EnumerateObject().Select(x => x.Name).Order());
    }

    private static SearchAvailabilityService Search(TestDatabase database, params IInterceptor[] interceptors) =>
        new(new AvailabilityReader(new BookingContextFactory(database, interceptors)), new BookingClock(At(9)));

    private static Dictionary<string, string?> Query() => new()
    {
        ["start"] = "2030-01-15T11:00:00Z",
        ["end"] = "2030-01-15T15:00:00Z",
        ["capacity"] = "50"
    };

    private static string Url(string start = "2030-01-15T11:00:00Z", string end = "2030-01-15T15:00:00Z",
        int? page = null, int? pageSize = null)
    {
        var query = Query();
        query["start"] = start;
        query["end"] = end;
        if (page.HasValue)
        {
            query["page"] = page.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (pageSize.HasValue)
        {
            query["pageSize"] = pageSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return QueryHelpers.AddQueryString("/rooms/availability", query);
    }

    private sealed class SearchReadObserver(bool failRooms = false, string? failure = null, bool pause = false) : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            if (pause)
            {
                command.CommandText = "SELECT pg_sleep(30)";
                Started.TrySetResult();
            }

            if (failure is not null && (failRooms ? Commands.Count == 2 : Commands.Count == 1))
            {
                throw failure switch
                {
                    "timeout" => new NpgsqlException("PRIVATE_PROVIDER_DETAILS", new TimeoutException()),
                    "schema" => new PostgresException("PRIVATE_SCHEMA_DETAILS", "ERROR", "ERROR", PostgresErrorCodes.UndefinedColumn),
                    _ => new ArgumentException("PRIVATE_CONFIGURATION_DETAILS")
                };
            }

            return ValueTask.FromResult(result);
        }
    }
}
