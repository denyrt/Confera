using Confera.Domain;
using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Confera.Infrastructure.Persistence;

public enum DemoInitializationResult { Completed, AlreadyCompleted, NonEmpty }

/// <summary>One seed identity, serialized and committed with its completion marker.</summary>
public sealed class DemoInitializer(IDbContextFactory<ConferaDbContext> factory, ILogger<DemoInitializer> logger)
{
    public const string SeedKey = "assignment-demo-v1";

    public async Task<DemoInitializationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        await using var strategyContext = await factory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async cancellationToken =>
        {
            // A replay never carries stale tracked rows or transaction state.
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(732109184621)", cancellationToken);

            if (await db.InitializationMarkers.AnyAsync(x => x.Key == SeedKey, cancellationToken))
            {
                logger.LogInformation("Demo initialization already completed.");
                return DemoInitializationResult.AlreadyCompleted;
            }

            if (await HasBusinessDataAsync(db, cancellationToken))
            {
                logger.LogWarning("Demo initialization skipped: unmarked database contains business data.");
                return DemoInitializationResult.NonEmpty;
            }

            db.Rooms.AddRange(CreateRooms());
            db.PricingRules.AddRange(CreateRules());
            db.InitializationMarkers.Add(new InitializationMarker
            {
                Key = SeedKey,
                CompletedAtUtc = UtcPrecision.Floor(DateTime.UtcNow)
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Demo initialization completed.");
            return DemoInitializationResult.Completed;
        }, cancellationToken);
    }

    private static async Task<bool> HasBusinessDataAsync(ConferaDbContext db, CancellationToken ct) =>
        await db.Rooms.AnyAsync(ct) || await db.Set<RoomService>().AnyAsync(ct)
        || await db.PricingRules.AnyAsync(ct) || await db.Bookings.AnyAsync(ct)
        || await db.Set<BookedRoomServiceSnapshot>().AnyAsync(ct) || await db.Set<BookingPriceSegment>().AnyAsync(ct);

    private static Room[] CreateRooms()
    {
        var a = new Room("Room A", 50, 2000m);
        a.SetServices([new("Projector", 500m), new("Wi-Fi", 300m)]);
        var b = new Room("Room B", 100, 3500m);
        b.SetServices([new("Projector", 500m), new("Wi-Fi", 300m), new("Sound", 700m)]);
        var c = new Room("Room C", 30, 1500m);
        c.SetServices([new("Wi-Fi", 300m)]);
        return [a, b, c];
    }

    private static BookingPricingRule[] CreateRules() =>
    [
        new("Morning", "Morning", new TimeOnly(6, 0), new TimeOnly(9, 0), 0.90m, 0),
        new("Standard", "Standard", new TimeOnly(9, 0), new TimeOnly(18, 0), 1m, 1),
        new("Evening", "Evening", new TimeOnly(18, 0), new TimeOnly(23, 0), 0.80m, 2),
        new("Peak", "Peak", new TimeOnly(12, 0), new TimeOnly(14, 0), 1.15m, 3)
    ];
}
