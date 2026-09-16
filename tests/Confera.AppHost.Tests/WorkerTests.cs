using System.Data.Common;
using System.Diagnostics;
using Confera.Infrastructure.Persistence;
using Confera.MigrationWorker;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Confera.AppHost.Tests;

public sealed class WorkerTests(PostgresFixture postgres)
{
    [Fact]
    public async Task WorkerRecoversFromTransientConnectionsThenMigratesAndStops()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrate: false);
        var fault = new ConnectionFaults(2);
        using var host = CreateHost(database, fault);
        var service = host.Services.GetRequiredService<MigrationService>();
        await host.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(0, service.ExitCode);
        Assert.Equal(2, fault.Failures);
        Assert.True(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        await using var db = database.Context();
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.Rooms.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PersistentTransientFailureStopsAfterSixRetries()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrate: false);
        var fault = new ConnectionFaults(int.MaxValue);
        using var host = CreateHost(database, fault);
        var service = host.Services.GetRequiredService<MigrationService>();
        var stopwatch = Stopwatch.StartNew();
        await host.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(45), TestContext.Current.CancellationToken);
        Assert.Equal(1, service.ExitCode);
        Assert.Equal(7, fault.Failures);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(40));
        Assert.All(fault.Attempts.Zip(fault.Attempts.Skip(1), (a, b) => b - a),
            delay => Assert.InRange(delay, TimeSpan.Zero, TimeSpan.FromSeconds(6)));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.InvalidPassword)]
    [InlineData(PostgresErrorCodes.InvalidSchemaName)]
    [InlineData(PostgresErrorCodes.CheckViolation)]
    public async Task PermanentProviderFailureIsNotRetried(string sqlState)
    {
        await using var database = await postgres.CreateDatabaseAsync(migrate: false);
        var fault = new ConnectionFaults(int.MaxValue, sqlState);
        using var host = CreateHost(database, fault);
        var service = host.Services.GetRequiredService<MigrationService>();
        await host.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(1, service.ExitCode);
        Assert.Equal(1, fault.Failures);
    }

    [Fact]
    public async Task ShutdownCancelsPendingDatabaseOperationAndReportsFailure()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrate: false);
        var pending = new PendingConnection();
        using var host = CreateHost(database, pending);
        var service = host.Services.GetRequiredService<MigrationService>();
        await host.StartAsync(TestContext.Current.CancellationToken);
        await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!;
        Assert.True(pending.Cancelled);
        Assert.Equal(1, service.ExitCode);
    }

    [Fact]
    public async Task OverallDeadlineCancelsAnOperationEvenWithoutHostShutdown()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrate: false);
        var pending = new PendingConnection();
        using var host = CreateHost(database, pending);
        var service = host.Services.GetRequiredService<MigrationService>();
        var stopwatch = Stopwatch.StartNew();
        await host.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(135), TestContext.Current.CancellationToken);
        Assert.True(pending.Cancelled);
        Assert.Equal(1, service.ExitCode);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(115), TimeSpan.FromSeconds(130));
    }

    private static IHost CreateHost(TestDatabase database, IInterceptor interceptor)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:confera"] = database.ConnectionString,
            ["DemoSeed:Enabled"] = "false"
        });
        builder.Services.AddConferaPersistence(builder.Configuration, workerRetries: true);
        var options = new DbContextOptionsBuilder<ConferaDbContext>(PersistenceConfiguration.Options(database.ConnectionString, true))
            .AddInterceptors(interceptor).Options;
        builder.Services.Replace(ServiceDescriptor.Singleton<IDbContextFactory<ConferaDbContext>>(new ContextFactory(options)));
        builder.Services.AddSingleton<DemoInitializer>();
        builder.Services.AddSingleton<MigrationService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<MigrationService>());
        return builder.Build();
    }

    private sealed class ContextFactory(DbContextOptions<ConferaDbContext> options) : IDbContextFactory<ConferaDbContext>
    {
        public ConferaDbContext CreateDbContext() => new(options);
    }

    private sealed class ConnectionFaults(int failuresBeforeSuccess, string? sqlState = null) : DbConnectionInterceptor
    {
        public int Failures { get; private set; }
        public List<TimeSpan> Attempts { get; } = [];
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Failures < failuresBeforeSuccess)
            {
                Failures++;
                Attempts.Add(_clock.Elapsed);
                if (sqlState is not null)
                {
                    throw new PostgresException("Injected permanent failure.", "ERROR", "ERROR", sqlState);
                }

                throw new NpgsqlException("Injected transient connection failure.", new IOException());
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class PendingConnection : DbConnectionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }

        public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return result;
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
        }
    }
}
