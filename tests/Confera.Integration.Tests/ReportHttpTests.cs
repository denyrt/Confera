using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Confera.Application.Availability;
using Confera.Application.Bookings;
using Confera.Application.Reports;
using Confera.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static Confera.Integration.Tests.BookingHttpTests;
using static Confera.Integration.Tests.BookingTestSupport;

namespace Confera.Integration.Tests;

public sealed class ReportHttpTests(PostgresFixture postgres)
{
    [Fact]
    public async Task RealDemoSeedSearchBookingAndReportsAgreeOnAssignmentPrices()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var initializer = new DemoInitializer(new BookingContextFactory(database), NullLogger<DemoInitializer>.Instance);
        Assert.Equal(DemoInitializationResult.Completed, await initializer.InitializeAsync(TestContext.Current.CancellationToken));
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        var available = (await client.GetFromJsonAsync<AvailabilityPage>(
            "/rooms/availability?start=2030-01-15T11:00:00Z&end=2030-01-15T15:00:00Z&capacity=1",
            TestContext.Current.CancellationToken))!;
        Assert.Equal(new[] { ("Room C", 30, 1500m), ("Room A", 50, 2000m), ("Room B", 100, 3500m) },
            available.Items.Select(x => (x.Name, x.Capacity, x.HourlyRate)));
        Assert.Equal(new[] { 1, 2, 3 }, available.Items.Select(x => x.Services.Count));
        Assert.Equal(new[] { ("Projector", 500m), ("Sound", 700m), ("Wi-Fi", 300m) },
            available.Items.Single(x => x.Name == "Room B").Services.OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => (x.Name, x.Price)));
        await using (var db = database.Context())
        {
            var rules = await db.PricingRules.OrderBy(x => x.Priority).ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(Rules().Select(x => (x.Code, x.StartsAt, x.EndsAt, x.Multiplier, x.Priority)),
                rules.Select(x => (x.Code, x.StartsAt, x.EndsAt, x.Multiplier, x.Priority)));
        }

        var room = available.Items.Single(x => x.Name == "Room A");
        using var bookingResponse = await client.PostAsJsonAsync("/bookings", new
        {
            roomId = room.Id,
            start = At(11),
            end = At(15),
            serviceIds = room.Services.Select(x => x.Id).ToArray()
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        var booking = (await bookingResponse.Content.ReadFromJsonAsync<BookingResult>(TestContext.Current.CancellationToken))!;
        Assert.Equal(9400m, booking.TotalPrice);

        var rooms = (await client.GetFromJsonAsync<RoomReport>(Url("rooms"), TestContext.Current.CancellationToken))!;
        Assert.Equal(new RoomReportRow(room.Id, "Room A", 1, 14400m, 8600m, 800m, 9400m), Assert.Single(rooms.Items));
        var services = (await client.GetFromJsonAsync<ServiceReport>(Url("services"), TestContext.Current.CancellationToken))!;
        Assert.Equal(new[] { new ServiceReportRow("Projector", 1, 500m), new ServiceReportRow("Wi-Fi", 1, 300m) }, services.Items);
    }

    [Theory]
    [InlineData("2000-01-01T00:00:00Z", "2001-01-01T00:00:00Z")]
    [InlineData("2040-01-01T00:00:00Z", "2040-01-01T00:00:00.000001Z")]
    [InlineData("2030-01-15T13:00:00+02:00", "2030-01-15T17:00:00+02:00")]
    [InlineData("2030-01-15T11:00Z", "2030-01-15T15:00Z")]
    [InlineData("2030-01-15T11:00:00.1234560000000000Z", "2030-01-15T15:00:00.123456Z")]
    public async Task BothReportsAcceptSharedTimestampProfileAndReturnEmptyUtcEnvelopes(string start, string end)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        foreach (var route in new[] { "rooms", "services" })
        {
            using var response = await client.GetAsync(Url(route, start, end), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var report = document.RootElement;
            Assert.Equal(new[] { "currency", "end", "items", "start" }, report.EnumerateObject().Select(x => x.Name).Order());
            Assert.Equal(DateTimeOffset.Parse(start, CultureInfo.InvariantCulture).UtcDateTime, report.GetProperty("start").GetDateTime());
            Assert.Equal(DateTimeOffset.Parse(end, CultureInfo.InvariantCulture).UtcDateTime, report.GetProperty("end").GetDateTime());
            Assert.Equal(DateTimeKind.Utc, report.GetProperty("start").GetDateTime().Kind);
            Assert.Equal("UAH", report.GetProperty("currency").GetString());
            Assert.Empty(report.GetProperty("items").EnumerateArray());
        }
    }

    [Theory]
    [InlineData("rooms")]
    [InlineData("services")]
    public async Task InvalidReportQueriesReturnSafeErrorsBeforeDatabaseReads(string route)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var observer = new ReportReadObserver();
        await using var factory = new BookingApiFactory(database, observer);
        using var client = factory.CreateHttpsClient();
        foreach (var field in new[] { "start", "end" })
        {
            foreach (var invalid in new string?[] { null, "", "2030-01-15T11:00:00", "2030-02-30T11:00:00Z",
                "2030-01-15T11:00:00.0000001Z", "2030-01-15T11:00:00.0000000000000001Z", "2030-01-15T11:00:00+99:00" })
            {
                var query = new Dictionary<string, string?> { ["start"] = "2030-01-15T11:00:00Z", ["end"] = "2030-01-15T15:00:00Z" };
                query[field] = invalid;
                using var response = await client.GetAsync(QueryHelpers.AddQueryString($"/reports/{route}", query), TestContext.Current.CancellationToken);
                await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_request");
            }
        }

        foreach (var end in new[] { "2030-01-15T11:00:00Z", "2030-01-15T10:59:59.999999Z" })
        {
            using var response = await client.GetAsync(Url(route, "2030-01-15T11:00:00Z", end), TestContext.Current.CancellationToken);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_report_period");
        }
        Assert.Empty(observer.Commands);
    }

    [Theory]
    [InlineData("rooms", "timeout", 503, "report_persistence_unavailable")]
    [InlineData("services", "timeout", 503, "report_persistence_unavailable")]
    [InlineData("rooms", "schema", 500, "internal_error")]
    [InlineData("services", "argument", 500, "internal_error")]
    public async Task ReportFailuresStaySafeAndAreNotRetried(string route, string failure, int status, string code)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var observer = new ReportReadObserver(failure);
        await using var factory = new BookingApiFactory(database, observer);
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync(Url(route), TestContext.Current.CancellationToken);
        await AssertProblemAsync(response, (HttpStatusCode)status, code);
        Assert.Single(observer.Commands);
    }

    [Fact]
    public async Task OpenApiDescribesBothReportsWithRequiredPeriodsAndTypedRows()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));
        var paths = document.RootElement.GetProperty("paths");
        foreach (var route in new[] { "rooms", "services" })
        {
            var operation = paths.GetProperty($"/reports/{route}").GetProperty("get");
            foreach (var status in new[] { "200", "400", "500", "503" })
            {
                Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
            }
            var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
            Assert.Equal(new[] { "end", "start" }, parameters.Select(x => x.GetProperty("name").GetString()).Order());
            Assert.All(parameters, x => Assert.True(x.GetProperty("required").GetBoolean()));
            Assert.Contains("without pagination", operation.GetProperty("description").GetString());
            Assert.Contains("[start, end)", operation.GetProperty("description").GetString());
        }

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var type in new[] { "RoomReport", "ServiceReport" })
        {
            Assert.Equal(new[] { "currency", "end", "items", "start" }, schemas.GetProperty(type).GetProperty("properties").EnumerateObject().Select(x => x.Name).Order());
        }
        Assert.Equal(new[] { "bookingCount", "rentalValue", "roomId", "roomName", "serviceValue", "totalBookedSeconds", "totalValue" },
            schemas.GetProperty("RoomReportRow").GetProperty("properties").EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(new[] { "selectionCount", "serviceName", "totalValue" },
            schemas.GetProperty("ServiceReportRow").GetProperty("properties").EnumerateObject().Select(x => x.Name).Order());
    }

    private static string Url(string route, string start = "2030-01-01T00:00:00Z", string end = "2030-02-01T00:00:00Z") =>
        QueryHelpers.AddQueryString($"/reports/{route}", new Dictionary<string, string?> { ["start"] = start, ["end"] = end });
}
