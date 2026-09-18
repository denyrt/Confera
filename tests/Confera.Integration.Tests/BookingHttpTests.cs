using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Confera.Application.Availability;
using Confera.Application.Bookings;
using Confera.Application.Reports;
using Confera.Application.Rooms;
using Confera.Domain.Rooms;
using Confera.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Confera.Integration.Tests.BookingTestSupport;

namespace Confera.Integration.Tests;

public sealed class BookingHttpTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("2030-01-15T11:00:00Z", "2030-01-15T15:00:00Z")]
    [InlineData("2030-01-15T13:00:00+02:00", "2030-01-15T17:00:00+02:00")]
    [InlineData("2030-01-15T11:00:00.0000000000000000Z", "2030-01-15T15:00:00.0000000Z")]
    public async Task PostNormalizesUtcAndReturnsPersistedPriceBreakdown(string start, string end)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        var payload = Payload(room);
        payload["start"] = start;
        payload["end"] = end;

        using var response = await client.PostAsJsonAsync("/bookings", payload, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var result = await response.Content.ReadFromJsonAsync<BookingResult>(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(At(11), result.Start);
        Assert.Equal(DateTimeKind.Utc, result.Start.Kind);
        Assert.Equal(At(15), result.End);
        Assert.Equal("UAH", result.Currency);
        Assert.Equal(9400m, result.TotalPrice);
        Assert.Equal(new[] { 2000m, 4600m, 2000m }, result.Segments.Select(x => x.Price));
        Assert.Equal(new[] { "Projector", "Wi-Fi" }, result.Services.Select(x => x.Name));

        await using var db = database.Context();
        var saved = await db.Bookings.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(result.BookingId, saved.Id);
        Assert.Equal(result.Start, saved.StartsAtUtc);
        Assert.Equal(result.End, saved.EndsAtUtc);
        Assert.Equal(result.TotalPrice, saved.TotalPrice);
    }

    [Fact]
    public async Task EmptySelectionAndMicrosecondEndpointsAreAcceptedButRepeatedPostConflicts()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        var payload = Payload(room);
        payload["serviceIds"] = new JsonArray();
        payload["start"] = "2030-01-15T11:00:00.1234560Z";
        payload["end"] = "2030-01-15T15:00:00.123456Z";

        using var first = await client.PostAsJsonAsync("/bookings", payload, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var result = (await first.Content.ReadFromJsonAsync<BookingResult>(TestContext.Current.CancellationToken))!;
        Assert.Empty(result.Services);
        Assert.Equal(At(11).AddTicks(1234560), result.Start);
        Assert.Equal(8600m, result.TotalPrice);
        using var second = await client.PostAsJsonAsync("/bookings", payload, TestContext.Current.CancellationToken);
        await AssertProblemAsync(second, HttpStatusCode.Conflict, "room_unavailable");
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Bookings\""));
    }

    [Fact]
    public async Task MalformedRequestsHaveConsistentSafeErrorsAndDoNotWrite()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();

        foreach (var field in new[] { "roomId", "start", "end", "serviceIds" })
        {
            var payload = Payload(room);
            payload.Remove(field);
            await Reject(payload);
        }

        foreach (var field in new[] { "start", "end", "serviceIds" })
        {
            var payload = Payload(room);
            payload[field] = null;
            await Reject(payload);
        }

        foreach (var badTime in new[]
        {
            "2030-01-15T11:00:00", "2030-01-15", "2030-01-15T11:00:00+99:00",
            "2030-01-15T11:00:00.0000001Z", "2030-01-15T11:00:00.00000001Z",
            "2030-01-15T11:00:00.1234560000000001Z", "2030-01-15T13:00:00.00000001+02:00"
        })
        {
            var payload = Payload(room);
            payload["start"] = badTime;
            await Reject(payload);
        }

        foreach (var badId in new[] { "invalid-uuid", Guid.Empty.ToString() })
        {
            var payload = Payload(room);
            payload["roomId"] = badId;
            await Reject(payload);
        }

        using var malformed = new StringContent("{broken PRIVATE_INPUT", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/bookings", malformed, TestContext.Current.CancellationToken);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_request");
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Bookings\""));

        async Task Reject(JsonObject payload)
        {
            using var rejected = await client.PostAsJsonAsync("/bookings", payload, TestContext.Current.CancellationToken);
            await AssertProblemAsync(rejected, HttpStatusCode.BadRequest, "invalid_request");
        }
    }

    [Theory]
    [InlineData("past", "invalid_booking_period")]
    [InlineData("short", "invalid_booking_period")]
    [InlineData("long", "invalid_booking_period")]
    [InlineData("reversed", "invalid_booking_period")]
    [InlineData("uncovered", "tariff_coverage_missing")]
    [InlineData("unknown_service", "invalid_service_selection")]
    [InlineData("other_service", "invalid_service_selection")]
    [InlineData("duplicate", "invalid_service_selection")]
    [InlineData("empty_service_id", "invalid_service_selection")]
    public async Task BusinessRejectionsHaveStableCodes(string scenario, string code)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, other) = await SeedAsync(database);
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        var payload = Payload(room);
        switch (scenario)
        {
            case "past": payload["start"] = "2030-01-15T08:00:00Z"; break;
            case "short": payload["end"] = "2030-01-15T11:29:00Z"; break;
            case "long": payload["end"] = "2030-01-16T11:01:00Z"; break;
            case "reversed": payload["end"] = "2030-01-15T10:00:00Z"; break;
            case "uncovered":
                payload["start"] = "2030-01-15T22:00:00Z";
                payload["end"] = "2030-01-16T00:00:00Z";
                break;
            case "unknown_service": payload["serviceIds"] = JsonSerializer.SerializeToNode(new[] { Guid.NewGuid() }); break;
            case "other_service": payload["serviceIds"] = JsonSerializer.SerializeToNode(new[] { other.Services.First().Id }); break;
            case "duplicate": payload["serviceIds"] = JsonSerializer.SerializeToNode(new[] { room.Services.First().Id, room.Services.First().Id }); break;
            case "empty_service_id": payload["serviceIds"] = JsonSerializer.SerializeToNode(new[] { Guid.Empty }); break;
        }

        using var response = await client.PostAsJsonAsync("/bookings", payload, TestContext.Current.CancellationToken);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Bookings\""));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAndDeletedRoomsReturnNotFound(bool deleted)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        var payload = Payload(room);
        if (deleted)
        {
            await using var db = database.Context();
            await db.Rooms.Where(x => x.Id == room.Id).ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.IsDeleted, true), TestContext.Current.CancellationToken);
        }
        else
        {
            payload["roomId"] = Guid.NewGuid().ToString();
        }

        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/bookings", payload, TestContext.Current.CancellationToken);
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "room_not_found");
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Bookings\""));
    }

    [Fact]
    public async Task ConcurrentHttpRequestsReturnCreatedAndConflict()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        var barrier = new RoomLockObserver(2);
        await using var factory = new BookingApiFactory(database, barrier);
        using var client = factory.CreateHttpsClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/bookings", Payload(room), timeout.Token),
            client.PostAsJsonAsync("/bookings", Payload(room), timeout.Token));
        using var first = responses[0];
        using var second = responses[1];
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        await AssertProblemAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict),
            HttpStatusCode.Conflict, "room_unavailable");
        Assert.Equal(2, barrier.ConnectionIds.Distinct().Count());
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Bookings\""));
    }

    [Theory]
    [InlineData("timeout", 503, "booking_persistence_unavailable")]
    [InlineData("command_timeout", 503, "booking_persistence_unavailable")]
    [InlineData("schema", 500, "internal_error")]
    [InlineData("argument", 500, "internal_error")]
    public async Task InfrastructureAndInternalFailuresStaySafeAndAreNotRetried(string failure, int status, string code)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        var interceptor = new FailTariffRead(failure);
        await using var factory = new BookingApiFactory(database, interceptor);
        using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/bookings", Payload(room), TestContext.Current.CancellationToken);
        await AssertProblemAsync(response, (HttpStatusCode)status, code);
        Assert.Equal(1, interceptor.Attempts);
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Bookings\""));
    }

    [Fact]
    public async Task OpenApiAndSwaggerDocumentBookingContract()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var operation = document.RootElement.GetProperty("paths").GetProperty("/bookings").GetProperty("post");
        foreach (var status in new[] { "201", "400", "404", "409", "500", "503" })
        {
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        }

        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CreateBookingRequest");
        Assert.Equal(new[] { "end", "roomId", "serviceIds", "start" },
            schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Order());
        Assert.Equal("date-time", schema.GetProperty("properties").GetProperty("start").GetProperty("format").GetString());
        using var ui = await client.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Contains("Swagger UI", await ui.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static JsonObject Payload(Room room) => new()
    {
        ["roomId"] = room.Id.ToString(),
        ["start"] = "2030-01-15T11:00:00Z",
        ["end"] = "2030-01-15T15:00:00Z",
        ["serviceIds"] = JsonSerializer.SerializeToNode(room.Services.Select(x => x.Id).ToArray())
    };

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(text);
        Assert.Equal((int)status, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("PRIVATE_", text);
        Assert.DoesNotContain("Npgsql", text);
        Assert.DoesNotContain("stackTrace", text);
        Assert.DoesNotContain("EX_Bookings", text);
    }

    private sealed class FailTariffRead(string failure) : DbCommandInterceptor
    {
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("BookingPricingRules", StringComparison.Ordinal))
            {
                Attempts++;
                if (failure == "command_timeout")
                {
                    command.CommandTimeout = 1;
                    command.CommandText = "SELECT pg_sleep(10)";
                    return ValueTask.FromResult(result);
                }

                throw failure switch
                {
                    "timeout" => new NpgsqlException("PRIVATE_PROVIDER_DETAILS", new TimeoutException("PRIVATE_TIMEOUT")),
                    "schema" => new PostgresException("PRIVATE_SCHEMA_DETAILS", "ERROR", "ERROR", PostgresErrorCodes.UndefinedColumn),
                    _ => new ArgumentException("PRIVATE_CONFIGURATION_DETAILS")
                };
            }

            return ValueTask.FromResult(result);
        }
    }
}

internal sealed class BookingApiFactory(TestDatabase database, params IInterceptor[] interceptors) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:confera", database.ConnectionString);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(new BookingClock(BookingTestSupport.At(9)));
            if (interceptors.Length > 0)
            {
                services.AddScoped<IBookingStore>(provider => new BookingStore(new BookingContextFactory(database, interceptors), provider.GetRequiredService<ILogger<BookingStore>>()));
                services.AddScoped<IRoomStore>(provider => new RoomStore(new BookingContextFactory(database, interceptors), provider.GetRequiredService<ILogger<RoomStore>>()));
                services.AddScoped<IAvailabilityReader>(provider => new AvailabilityReader(new BookingContextFactory(database, interceptors), provider.GetRequiredService<ILogger<AvailabilityReader>>()));
                services.AddScoped<IReportReader>(provider => new ReportReader(new BookingContextFactory(database, interceptors), provider.GetRequiredService<ILogger<ReportReader>>()));
            }
        });
    }

    internal HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });
}
