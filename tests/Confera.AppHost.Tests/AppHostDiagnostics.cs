using System.Collections.Concurrent;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Confera.AppHost.Tests;

/// <summary>Archive resource states without environment variables, credentials or connection strings.</summary>
internal sealed class AppHostDiagnostics : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<object> _events = new();
    private readonly Task _watch;

    public AppHostDiagnostics(DistributedApplication app)
    {
        _watch = WatchAsync(app);
    }

    private async Task WatchAsync(DistributedApplication app)
    {
        try
        {
            await foreach (var update in app.ResourceNotifications.WatchAsync(_stop.Token))
            {
                _events.Enqueue(new
                {
                    TimeUtc = DateTime.UtcNow,
                    Resource = update.Resource.Name,
                    State = update.Snapshot.State?.Text,
                    update.Snapshot.ExitCode,
                    Health = update.Snapshot.HealthStatus?.ToString()
                });
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _watch;
        _stop.Dispose();
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Confera.slnx")))
        {
            repository = repository.Parent;
        }

        var output = Path.Combine(repository?.FullName ?? AppContext.BaseDirectory, "artifacts", "diagnostics");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, $"apphost-{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(_events, new JsonSerializerOptions { WriteIndented = true }));
    }
}
