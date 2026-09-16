using Confera.Application.Bookings;
using Confera.Application.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Confera.Infrastructure.Persistence;

public static class PersistenceConfiguration
{
    public const string ConnectionName = "confera";

    public static DbContextOptions<ConferaDbContext> Options(string connectionString, bool workerRetries = false)
    {
        var builder = new DbContextOptionsBuilder<ConferaDbContext>();
        Configure(builder, connectionString, workerRetries);
        return builder.Options;
    }

    private static void Configure(DbContextOptionsBuilder builder, string connectionString, bool workerRetries)
    {
        // Set before any data source is created, including design time and tests.
        AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", true);
        builder.UseNpgsql(connectionString, pg =>
            {
                if (workerRetries)
                {
                    pg.EnableRetryOnFailure(6, TimeSpan.FromSeconds(5), null);
                }
            });
    }

    public static IServiceCollection AddConferaPersistence(this IServiceCollection services, IConfiguration configuration, bool workerRetries = false)
    {
        var connection = configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException("ConnectionStrings:confera is required.");
        services.AddDbContextFactory<ConferaDbContext>(options => Configure(options, connection, workerRetries));
        services.AddScoped<IBookingStore, BookingStore>();
        services.AddScoped<IRoomStore, RoomStore>();
        return services;
    }

    public static IServiceCollection AddConferaDatabaseHealth(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("confera-database");
        return services;
    }
}

internal sealed class DatabaseHealthCheck(IDbContextFactory<ConferaDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Database unavailable.");
    }
}

public sealed class ConferaDesignTimeFactory : IDesignTimeDbContextFactory<ConferaDbContext>
{
    public ConferaDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
        var connection = configuration.GetConnectionString(PersistenceConfiguration.ConnectionName)
            ?? throw new InvalidOperationException("Set ConnectionStrings__confera for design-time EF commands.");
        return new ConferaDbContext(PersistenceConfiguration.Options(connection));
    }
}
