using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Confera.Application.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using static Confera.Integration.Tests.BookingHttpTests;
using static Confera.Integration.Tests.BookingTestSupport;

namespace Confera.Integration.Tests;

public sealed class RoomHttpTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CreateReadReplaceAndDeleteRoundTripWithStableServiceIdentityAndEtags()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        using var created = await client.PostAsJsonAsync("/rooms", Payload(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var room = (await created.Content.ReadFromJsonAsync<RoomResult>(TestContext.Current.CancellationToken))!;
        var uri = $"/rooms/{room.Id}";
        Assert.Equal(uri, created.Headers.Location!.AbsolutePath);
        Assert.Equal("Room", room.Name);
        Assert.Equal("UAH", room.Currency);
        var projectorId = room.Services.Single(x => x.Name == "Projector").Id;
        var wifiId = room.Services.Single(x => x.Name == "Wi-Fi").Id;
        var (_, originalTag) = await Read(client, uri);

        var replacement = Payload();
        replacement["hourlyRate"] = 2500m;
        replacement["services"] = JsonSerializer.SerializeToNode(new[] { new { name = "projector", price = 600m }, new { name = "Internet", price = 400m } });
        using var updated = await Send(client, HttpMethod.Put, uri, replacement, originalTag);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Null(updated.Headers.ETag);
        var (current, newTag) = await Read(client, uri);
        Assert.NotEqual(originalTag, newTag);
        Assert.Equal(projectorId, current.Services.Single(x => x.Name == "projector").Id);
        Assert.DoesNotContain(current.Services, x => x.Id == wifiId);
        Assert.Equal(2500m, current.HourlyRate);

        using var stale = await Send(client, HttpMethod.Put, uri, Payload(), originalTag);
        await AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, "room_version_mismatch");
        using var noop = await Send(client, HttpMethod.Put, uri, replacement, newTag);
        Assert.Equal(HttpStatusCode.OK, noop.StatusCode);
        Assert.Equal(newTag, (await Read(client, uri)).Tag);

        using var missingDelete = await Send(client, HttpMethod.Delete, uri, null, null);
        await AssertProblemAsync(missingDelete, (HttpStatusCode)428, "room_precondition_required");
        using var staleDelete = await Send(client, HttpMethod.Delete, uri, null, originalTag);
        await AssertProblemAsync(staleDelete, HttpStatusCode.PreconditionFailed, "room_version_mismatch");
        using var deleted = await Send(client, HttpMethod.Delete, uri, null, newTag);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var repeated = await Send(client, HttpMethod.Delete, uri, null, newTag);
        using var repeatedWithoutTag = await Send(client, HttpMethod.Delete, uri, null, null);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeatedWithoutTag.StatusCode);
        using var getDeleted = await client.GetAsync(uri, TestContext.Current.CancellationToken);
        await AssertProblemAsync(getDeleted, HttpStatusCode.NotFound, "room_not_found");
        using var updateDeleted = await Send(client, HttpMethod.Put, uri, replacement, newTag);
        await AssertProblemAsync(updateDeleted, HttpStatusCode.NotFound, "room_not_found");
        using var unknown = await Send(client, HttpMethod.Delete, $"/rooms/{Guid.NewGuid()}", null, newTag);
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "room_not_found");

        await using var db = database.Context();
        var saved = await db.Rooms.Include(x => x.Services).SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(saved.IsDeleted);
        Assert.Equal(2, saved.Services.Count);
        using var reused = await client.PostAsJsonAsync("/rooms", Payload(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, reused.StatusCode);
        Assert.NotEqual(room.Id, (await reused.Content.ReadFromJsonAsync<RoomResult>(TestContext.Current.CancellationToken))!.Id);
    }

    [Theory]
    [InlineData(null, 428, "room_precondition_required")]
    [InlineData("unquoted", 400, "invalid_request")]
    [InlineData("", 428, "room_precondition_required")]
    [InlineData("\"\"", 412, "room_version_mismatch")]
    [InlineData("*", 400, "invalid_request")]
    [InlineData("\"unknown\"", 412, "room_version_mismatch")]
    [InlineData("weak_current", 412, "room_version_mismatch")]
    public async Task InvalidAndUnmatchedPreconditionsDoNotChangeRoom(string? header, int status, string code)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        var uri = $"/rooms/{room.Id}";
        var (_, tag) = await Read(client, uri);
        using var response = await Send(client, HttpMethod.Put, uri, Payload(), header == "weak_current" ? "W/" + tag : header);
        await AssertProblemAsync(response, (HttpStatusCode)status, code);
        if (status == 428) Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(tag, (await Read(client, uri)).Tag);
    }

    [Fact]
    public async Task ActuallyPresentEmptyHeaderIsMalformed()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        await using var factory = new BookingApiFactory(database);
        // HttpClient drops an empty If-Match value. Exercise an actually present empty
        // field through TestServer so it is distinguished from an absent precondition.
        var body = JsonSerializer.SerializeToUtf8Bytes(Payload());
        using var input = new MemoryStream(body);
        var context = await factory.Server.SendAsync(context =>
        {
            context.Request.Scheme = "https";
            context.Request.Method = "PUT";
            context.Request.Path = $"/rooms/{room.Id}";
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = body.Length;
            context.Request.Headers["If-Match"] = string.Empty;
            context.Request.Body = input;
        }, TestContext.Current.CancellationToken);
        Assert.Equal(400, context.Response.StatusCode);
        await using var db = database.Context();
        Assert.Equal(room.Version, (await db.Rooms.SingleAsync(x => x.Id == room.Id, TestContext.Current.CancellationToken)).Version);
    }

    [Fact]
    public async Task ListsAndRepeatedHeadersUseStrongComparisonAndSupportEmptyServiceSet()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        var uri = $"/rooms/{room.Id}";
        var (_, tag) = await Read(client, uri);
        var payload = Payload();
        payload["services"] = new JsonArray();
        using var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = JsonContent.Create(payload) };
        request.Headers.TryAddWithoutValidation("If-Match", new[] { "\"unknown,opaque\", W/" + tag, tag });
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await Read(client, uri)).Room.Services);
    }

    [Fact]
    public async Task InvalidBodiesRejectRequiredFieldsAndServiceValuesWithoutPartialWrites()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        foreach (var field in new[] { "name", "capacity", "hourlyRate", "services" })
        {
            var omitted = Payload();
            omitted.Remove(field);
            using var response = await client.PostAsJsonAsync("/rooms", omitted, TestContext.Current.CancellationToken);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_request");
            var nullValue = Payload();
            nullValue[field] = null;
            using var nullResponse = await client.PostAsJsonAsync("/rooms", nullValue, TestContext.Current.CancellationToken);
            await AssertProblemAsync(nullResponse, HttpStatusCode.BadRequest, "invalid_request");
        }

        foreach (var services in new[] { "[null]", "[{\"name\":\"Service\"}]", "[{\"price\":300}]" })
        {
            var payload = Payload();
            payload["services"] = JsonNode.Parse(services);
            using var response = await client.PostAsJsonAsync("/rooms", payload, TestContext.Current.CancellationToken);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_request");
        }

        foreach (var services in new[]
        {
            "[{\"name\":\"Projector\",\"price\":600},{\"name\":\" projector \",\"price\":700}]",
            "[{\"name\":\"Service\",\"price\":300.0001}]"
        })
        {
            var payload = Payload();
            payload["services"] = JsonNode.Parse(services);
            using var response = await client.PostAsJsonAsync("/rooms", payload, TestContext.Current.CancellationToken);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_room_data");
        }

        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Rooms\""));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"RoomServices\""));
    }

    [Fact]
    public async Task NameAndLifecycleConflictsAreSafeAndUpdatesPreserveSnapshots()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, other) = await SeedAsync(database);
        var booking = await Service(database).CreateAsync(Command(room), TestContext.Current.CancellationToken);
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        var uri = $"/rooms/{room.Id}";
        var (_, tag) = await Read(client, uri);
        var duplicate = Payload();
        duplicate["name"] = " room b ";
        duplicate["services"] = new JsonArray();
        using var conflict = await Send(client, HttpMethod.Put, uri, duplicate, tag);
        await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "room_name_conflict");
        Assert.Equal(tag, (await Read(client, uri)).Tag);
        Assert.Equal(2, (await Read(client, uri)).Room.Services.Count);
        var reduce = Payload();
        reduce["capacity"] = 40;
        using var reduction = await Send(client, HttpMethod.Put, uri, reduce, tag);
        using var deletion = await Send(client, HttpMethod.Delete, uri, null, tag);
        await AssertProblemAsync(reduction, HttpStatusCode.Conflict, "room_has_unfinished_bookings");
        await AssertProblemAsync(deletion, HttpStatusCode.Conflict, "room_has_unfinished_bookings");

        var allowed = Payload();
        allowed["name"] = "Renamed";
        allowed["hourlyRate"] = 2500m;
        allowed["services"] = new JsonArray();
        using var changed = await Send(client, HttpMethod.Put, uri, allowed, tag);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        await using var db = database.Context();
        var saved = await db.Bookings.Include(x => x.Services).Include(x => x.PriceSegments).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(booking.TotalPrice, saved.TotalPrice);
        Assert.Equal(2000m, saved.HourlyRateSnapshot);
        Assert.Equal(new[] { "Projector", "Wi-Fi" }, saved.Services.Select(x => x.ServiceNameSnapshot).Order());
        Assert.Equal(8600m, saved.PriceSegments.Sum(x => x.Price));
    }

    [Fact]
    public async Task ServiceOnlyChangesRotatePersistedVersionWhileOrderAndDecimalScaleDoNot()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        var uri = $"/rooms/{room.Id}";
        var (_, tag) = await Read(client, uri);
        var payload = Payload();
        payload["name"] = room.Name;
        payload["services"] = JsonSerializer.SerializeToNode(new[] { new { name = "Wi-Fi", price = 300.000m }, new { name = "Projector", price = 500.000m } });
        using var noop = await Send(client, HttpMethod.Put, uri, payload, tag);
        Assert.Equal(HttpStatusCode.OK, noop.StatusCode);
        Assert.Equal(tag, (await Read(client, uri)).Tag);

        payload["services"] = JsonSerializer.SerializeToNode(new[] { new { name = "Wi-Fi", price = 300m }, new { name = "PROJECTOR", price = 500m } });
        using var changed = await Send(client, HttpMethod.Put, uri, payload, tag);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var (current, nextTag) = await Read(client, uri);
        Assert.NotEqual(tag, nextTag);
        Assert.Equal(room.Services.Single(x => x.Name == "Projector").Id, current.Services.Single(x => x.Name == "PROJECTOR").Id);
        using var stale = await Send(client, HttpMethod.Put, uri, payload, tag);
        await AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, "room_version_mismatch");
    }

    [Fact]
    public async Task ConcurrentEditsWithSameTagReturnOneSuccessAndOnePreconditionFailure()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        var observer = new RoomLockObserver(2);
        await using var factory = new BookingApiFactory(database, observer);
        using var client = factory.CreateHttpsClient();
        var uri = $"/rooms/{room.Id}";
        var (_, tag) = await Read(client, uri);
        var first = Payload();
        first["name"] = "First";
        var second = Payload();
        second["name"] = "Second";
        var responses = await Task.WhenAll(Send(client, HttpMethod.Put, uri, first, tag), Send(client, HttpMethod.Put, uri, second, tag));
        using var a = responses[0];
        using var b = responses[1];
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        await AssertProblemAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.PreconditionFailed),
            HttpStatusCode.PreconditionFailed, "room_version_mismatch");
        Assert.Equal(2, observer.ConnectionIds.Distinct().Count());
        var winner = (await responses.Single(x => x.IsSuccessStatusCode).Content.ReadFromJsonAsync<RoomResult>(TestContext.Current.CancellationToken))!;
        Assert.Equal(winner.Name, (await Read(client, uri)).Room.Name);
    }

    [Fact]
    public async Task ConcurrentCreatesWithEquivalentNamesAreAtomic()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        var first = Payload();
        var second = Payload();
        second["name"] = " room ";
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/rooms", first, TestContext.Current.CancellationToken),
            client.PostAsJsonAsync("/rooms", second, TestContext.Current.CancellationToken));
        using var a = responses[0];
        using var b = responses[1];
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        await AssertProblemAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "room_name_conflict");
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Rooms\""));
        Assert.Equal(2L, await database.ScalarAsync<long>("SELECT count(*) FROM \"RoomServices\""));
    }

    [Theory]
    [InlineData("timeout", 503, "room_persistence_unavailable")]
    [InlineData("command_timeout", 503, "room_persistence_unavailable")]
    [InlineData("schema", 500, "internal_error")]
    public async Task PersistenceFailuresAreSafeAndNotRetried(string failure, int status, string code)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        var interceptor = new FailRoomLock(failure);
        await using var factory = new BookingApiFactory(database, interceptor);
        using var client = factory.CreateHttpsClient();
        var uri = $"/rooms/{room.Id}";
        var (_, tag) = await Read(client, uri);
        using var response = await Send(client, HttpMethod.Put, uri, Payload(), tag);
        await AssertProblemAsync(response, (HttpStatusCode)status, code);
        Assert.Equal(1, interceptor.Attempts);
        Assert.Equal(tag, (await Read(client, uri)).Tag);
    }

    [Fact]
    public async Task OpenApiExposesAllRoomOperationsAndConditionalHeaders()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BookingApiFactory(database);
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/rooms").TryGetProperty("post", out _));
        var roomPath = paths.GetProperty("/rooms/{id}");
        Assert.Contains("ETag", roomPath.GetProperty("get").GetProperty("description").GetString());
        foreach (var method in new[] { "put", "delete" })
        {
            var operation = roomPath.GetProperty(method);
            Assert.True(operation.GetProperty("responses").TryGetProperty("412", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("428", out _));
            Assert.Contains(operation.GetProperty("parameters").EnumerateArray(),
                x => x.GetProperty("name").GetString() == "If-Match" && x.GetProperty("in").GetString() == "header");
        }
    }

    private static JsonObject Payload() => new()
    {
        ["name"] = " Room ",
        ["capacity"] = 50,
        ["hourlyRate"] = 2000m,
        ["services"] = JsonSerializer.SerializeToNode(new[] { new { name = "Projector", price = 500m }, new { name = "Wi-Fi", price = 300m } })
    };

    private static async Task<(RoomResult Room, string Tag)> Read(HttpClient client, string uri)
    {
        using var response = await client.GetAsync(uri, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag.IsWeak);
        return ((await response.Content.ReadFromJsonAsync<RoomResult>(TestContext.Current.CancellationToken))!, response.Headers.ETag.ToString());
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string uri, JsonObject? payload, string? tag)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        if (tag is not null) request.Headers.TryAddWithoutValidation("If-Match", tag);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class FailRoomLock(string failure) : DbCommandInterceptor
    {
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                Attempts++;
                if (failure == "command_timeout")
                {
                    command.CommandTimeout = 1;
                    command.CommandText = "SELECT pg_sleep(10)";
                }
                else
                {
                    throw failure == "timeout"
                        ? new NpgsqlException("PRIVATE_DETAILS", new TimeoutException("PRIVATE_TIMEOUT"))
                        : new PostgresException("PRIVATE_DETAILS", "ERROR", "ERROR", PostgresErrorCodes.UndefinedColumn);
                }
            }

            return ValueTask.FromResult(result);
        }
    }
}
