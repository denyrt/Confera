using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Confera.Integration.Tests.BookingHttpTests;
using static Confera.Integration.Tests.BookingTestSupport;

namespace Confera.Integration.Tests;

public sealed class ResultBoundaryHttpTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("booking", false, "booking_persistence_unavailable", "BookingStore")]
    [InlineData("room", false, "room_persistence_unavailable", "RoomStore")]
    [InlineData("availability", false, "availability_persistence_unavailable", "AvailabilityReader")]
    [InlineData("report", false, "report_persistence_unavailable", "ReportReader")]
    [InlineData("report", true, "internal_error", "ApiExceptionHandler")]
    public async Task TechnicalFailuresKeepOriginalDiagnosticsAndRequestCorrelation(
        string feature, bool unexpected, string code, string loggingOwner)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var (room, _) = await SeedAsync(database);
        var failure = unexpected
            ? (Exception)new InvalidOperationException("PRIVATE_CONFIGURATION_DETAILS")
            : new NpgsqlException("PRIVATE_PROVIDER_DETAILS", new TimeoutException());
        using var logs = new CapturedLogs();
        await using var original = new BookingApiFactory(database, new FailingRead(failure));
        await using var factory = original.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var period = "start=2030-01-15T11:00:00Z&end=2030-01-15T15:00:00Z";
        using var response = feature switch
        {
            "booking" => await client.PostAsJsonAsync("/bookings",
                new { roomId = room.Id, start = At(11), end = At(15), serviceIds = Array.Empty<Guid>() },
                TestContext.Current.CancellationToken),
            "room" => await client.GetAsync($"/rooms/{room.Id}", TestContext.Current.CancellationToken),
            "availability" => await client.GetAsync($"/rooms/availability?{period}&capacity=50", TestContext.Current.CancellationToken),
            _ => await client.GetAsync($"/reports/rooms?{period}", TestContext.Current.CancellationToken)
        };

        await AssertProblemAsync(response, unexpected ? HttpStatusCode.InternalServerError : HttpStatusCode.ServiceUnavailable, code);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var entry = Assert.Single(logs.Entries);
        Assert.EndsWith(loggingOwner, entry.Category);
        Assert.True(ContainsException(entry.Exception, failure), "The log must retain the original exception in its cause chain.");
        Assert.Equal(entry.ActivityId, problem.RootElement.GetProperty("traceId").GetString());
        Assert.Contains(entry.Scopes, x => x.Key == "RequestId" && !string.IsNullOrEmpty(x.Value?.ToString()));
        Assert.Contains(entry.Scopes, x => x.Key == "TraceId"
            && entry.ActivityId!.Contains(x.Value!.ToString()!, StringComparison.Ordinal));

        // Expected responses retain the cache headers previously supplied by exception middleware.
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(response.Headers.Pragma, x => x.Name == "no-cache");
        Assert.Equal("-1", Assert.Single(response.Content.Headers.GetValues("Expires")));
    }

    private sealed class FailingRead(Exception failure) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            throw failure;
    }

    private static bool ContainsException(Exception? error, Exception expected)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            if (ReferenceEquals(current, expected)) return true;
        }

        return false;
    }

    private sealed record LogEntry(string Category, Exception? Exception, string? ActivityId,
        List<KeyValuePair<string, object?>> Scopes);

    private sealed class CapturedLogs : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
        public void Dispose() { }

        private sealed class CaptureLogger(CapturedLogs owner, string category) : ILogger
        {
            public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Error && category.StartsWith("Confera.", StringComparison.Ordinal);
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                var scopes = new List<KeyValuePair<string, object?>>();
                owner._scopes.ForEachScope((scope, items) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> properties) items.AddRange(properties);
                }, scopes);
                owner.Entries.Enqueue(new(category, exception, Activity.Current?.Id, scopes));
            }
        }
    }
}
