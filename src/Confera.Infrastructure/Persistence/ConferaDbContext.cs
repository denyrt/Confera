using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using Microsoft.EntityFrameworkCore;

namespace Confera.Infrastructure.Persistence;

public sealed class ConferaDbContext(DbContextOptions<ConferaDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingPricingRule> PricingRules => Set<BookingPricingRule>();
    public DbSet<InitializationMarker> InitializationMarkers => Set<InitializationMarker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConferaDbContext).Assembly);
    }
}

public sealed class InitializationMarker
{
    public string Key { get; init; } = null!;
    public DateTime CompletedAtUtc { get; init; }
}
