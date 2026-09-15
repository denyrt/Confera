using Confera.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Confera.MigrationWorker;

public sealed class MigrationService(
    IDbContextFactory<ConferaDbContext> factory,
    DemoInitializer initializer,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    ILogger<MigrationService> logger) : BackgroundService
{
    // Success is explicit: shutdown before completion must remain a failed run.
    public int ExitCode { get; private set; } = 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        var phase = "migration";
        try
        {
            logger.LogInformation("Applying schema migrations.");
            await using var strategyContext = await factory.CreateDbContextAsync(deadline.Token);
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            // Npgsql reads migration history before EF's internal retry scope.
            // Cover that preflight too; EF still owns migration transactions/locks.
            await strategy.ExecuteAsync(async cancellationToken =>
            {
                await using var db = await factory.CreateDbContextAsync(cancellationToken);
                await db.Database.MigrateAsync(cancellationToken);
            }, deadline.Token);
            phase = "demo initialization";
            if (configuration.GetValue<bool>("DemoSeed:Enabled"))
            {
                await initializer.InitializeAsync(deadline.Token);
            }

            deadline.Token.ThrowIfCancellationRequested();
            logger.LogInformation("Persistence initialization completed successfully.");
            ExitCode = 0;
        }
        catch (Exception error)
        {
            // Avoid logging connection strings or provider message/detail containing data.
            logger.LogError("Persistence {Phase} failed ({ErrorType}, SQLSTATE {SqlState}).",
                phase, error.GetType().Name, FindSqlState(error));
        }
        finally
        {
            lifetime.StopApplication();
        }
    }

    private static string? FindSqlState(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is PostgresException providerError)
            {
                return providerError.SqlState;
            }
        }

        return null;
    }
}
