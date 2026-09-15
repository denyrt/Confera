using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Confera.Infrastructure.Persistence;

internal static class ColumnRules
{
    internal static void Identity<T>(EntityTypeBuilder<T> builder, string table) where T : class
    {
        builder.ToTable(table, t => t.HasCheckConstraint($"CK_{table}_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey("Id").HasName($"PK_{table}");
        builder.Property<Guid>("Id").ValueGeneratedNever();
    }

    internal static void Text<T>(EntityTypeBuilder<T> builder, string table, string column) where T : class
    {
        builder.Property<string>(column).HasColumnType("text").IsRequired();
        builder.ToTable(table, t => t.HasCheckConstraint($"CK_{table}_{column}",
            $"confera_utf16_length(\"{column}\") <= 64 AND confera_name_key(\"{column}\") <> ''"));
    }

    internal static void Number<T>(EntityTypeBuilder<T> builder, string table, string column, string minimum, string maximum, int scale = 3) where T : class
    {
        builder.Property<decimal>(column).HasColumnType("numeric").IsRequired();
        builder.ToTable(table, t => t.HasCheckConstraint($"CK_{table}_{column}",
            $"\"{column}\" BETWEEN {minimum} AND {maximum} AND \"{column}\" = round(\"{column}\", {scale})"));
    }

    internal static void Instant<T>(EntityTypeBuilder<T> builder, string table, string column) where T : class
    {
        builder.Property<DateTime>(column).HasColumnType("timestamp(6) with time zone").IsRequired();
        builder.ToTable(table, t => t.HasCheckConstraint($"CK_{table}_{column}",
            $"isfinite(\"{column}\") AND \"{column}\" BETWEEN TIMESTAMPTZ '0001-01-01 00:00:00+00' AND TIMESTAMPTZ '9999-12-31 23:59:59.999999+00'"));
    }

    internal static void NameKey<T>(EntityTypeBuilder<T> builder) where T : class =>
        builder.Property<string>("NameKey").HasColumnType("text").UseCollation("C")
            .HasComputedColumnSql("confera_name_key(\"Name\")", stored: true).IsRequired();
}

internal sealed class RoomMapping : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> b)
    {
        ColumnRules.Identity(b, "Rooms");
        ColumnRules.Text(b, "Rooms", nameof(Room.Name));
        ColumnRules.NameKey(b);
        b.Property(x => x.Capacity).IsRequired();
        b.ToTable("Rooms", t => t.HasCheckConstraint("CK_Rooms_Capacity", "\"Capacity\" > 0"));
        ColumnRules.Number(b, "Rooms", nameof(Room.HourlyRate), "1000", "100000");
        b.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();
        b.HasIndex("NameKey").IsUnique().HasDatabaseName("UX_Rooms_ActiveName").HasFilter("\"IsDeleted\" = false");
        b.HasMany(x => x.Services).WithOne().HasForeignKey(x => x.RoomId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_RoomServices_Rooms");
        b.Navigation(x => x.Services).HasField("_services").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RoomServiceMapping : IEntityTypeConfiguration<RoomService>
{
    public void Configure(EntityTypeBuilder<RoomService> b)
    {
        ColumnRules.Identity(b, "RoomServices");
        b.Property(x => x.RoomId).IsRequired();
        ColumnRules.Text(b, "RoomServices", nameof(RoomService.Name));
        ColumnRules.NameKey(b);
        ColumnRules.Number(b, "RoomServices", nameof(RoomService.Price), "200", "20000");
        b.HasIndex("RoomId", "NameKey").IsUnique().HasDatabaseName("UX_RoomServices_RoomName");
    }
}

internal sealed class PricingRuleMapping : IEntityTypeConfiguration<BookingPricingRule>
{
    public void Configure(EntityTypeBuilder<BookingPricingRule> b)
    {
        ColumnRules.Identity(b, "BookingPricingRules");
        ColumnRules.Text(b, "BookingPricingRules", nameof(BookingPricingRule.Code));
        ColumnRules.Text(b, "BookingPricingRules", nameof(BookingPricingRule.Name));
        ColumnRules.Number(b, "BookingPricingRules", nameof(BookingPricingRule.Multiplier), "0.50", "2.00", 2);
        b.Property(x => x.StartsAt).HasColumnType("time(6) without time zone").IsRequired();
        b.Property(x => x.EndsAt).HasColumnType("time(6) without time zone").IsRequired();
        b.ToTable("BookingPricingRules", t => t.HasCheckConstraint("CK_BookingPricingRules_DailyInterval",
            "\"StartsAt\" >= TIME '00:00' AND \"StartsAt\" < TIME '24:00' AND \"EndsAt\" >= TIME '00:00' AND \"EndsAt\" < TIME '24:00' AND \"StartsAt\" <> \"EndsAt\""));
        b.Property(x => x.Priority).IsRequired();
        b.HasAlternateKey(x => x.Priority).HasName("UQ_BookingPricingRules_Priority");
    }
}

internal sealed class BookingMapping : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> b)
    {
        ColumnRules.Identity(b, "Bookings");
        b.Property(x => x.RoomId).IsRequired();
        ColumnRules.Instant(b, "Bookings", nameof(Booking.StartsAtUtc));
        ColumnRules.Instant(b, "Bookings", nameof(Booking.EndsAtUtc));
        ColumnRules.Instant(b, "Bookings", nameof(Booking.CreatedAtUtc));
        ColumnRules.Number(b, "Bookings", nameof(Booking.HourlyRateSnapshot), "1000", "100000");
        ColumnRules.Number(b, "Bookings", nameof(Booking.TotalPrice), "0", "999999999999999.999");
        b.ToTable("Bookings", t => t.HasCheckConstraint("CK_Bookings_Duration",
            "\"EndsAtUtc\" - \"StartsAtUtc\" BETWEEN INTERVAL '30 minutes' AND INTERVAL '24 hours'"));
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Bookings_Rooms");
        b.HasIndex(x => new { x.RoomId, x.StartsAtUtc, x.EndsAtUtc }).HasDatabaseName("IX_Bookings_RoomPeriod");
        b.HasMany(x => x.Services).WithOne().HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_BookedRoomServiceSnapshots_Bookings");
        b.HasMany(x => x.PriceSegments).WithOne().HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_BookingPriceSegments_Bookings");
        b.Navigation(x => x.Services).HasField("_services").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.PriceSegments).HasField("_priceSegments").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ServiceSnapshotMapping : IEntityTypeConfiguration<BookedRoomServiceSnapshot>
{
    public void Configure(EntityTypeBuilder<BookedRoomServiceSnapshot> b)
    {
        ColumnRules.Identity(b, "BookedRoomServiceSnapshots");
        b.Property(x => x.BookingId).IsRequired();
        ColumnRules.Text(b, "BookedRoomServiceSnapshots", nameof(BookedRoomServiceSnapshot.ServiceNameSnapshot));
        ColumnRules.Number(b, "BookedRoomServiceSnapshots", nameof(BookedRoomServiceSnapshot.ServicePriceSnapshot), "200", "20000");
        b.HasIndex(x => x.BookingId).HasDatabaseName("IX_BookedRoomServiceSnapshots_BookingId");
    }
}

internal sealed class PriceSegmentMapping : IEntityTypeConfiguration<BookingPriceSegment>
{
    public void Configure(EntityTypeBuilder<BookingPriceSegment> b)
    {
        ColumnRules.Identity(b, "BookingPriceSegments");
        b.Property(x => x.BookingId).IsRequired();
        ColumnRules.Instant(b, "BookingPriceSegments", nameof(BookingPriceSegment.StartsAtUtc));
        ColumnRules.Instant(b, "BookingPriceSegments", nameof(BookingPriceSegment.EndsAtUtc));
        ColumnRules.Text(b, "BookingPriceSegments", nameof(BookingPriceSegment.PricingCodeSnapshot));
        ColumnRules.Number(b, "BookingPriceSegments", nameof(BookingPriceSegment.HourlyRateSnapshot), "1000", "100000");
        ColumnRules.Number(b, "BookingPriceSegments", nameof(BookingPriceSegment.MultiplierSnapshot), "0.50", "2.00", 2);
        ColumnRules.Number(b, "BookingPriceSegments", nameof(BookingPriceSegment.Price), "0", "4800000");
        b.ToTable("BookingPriceSegments", t => t.HasCheckConstraint("CK_BookingPriceSegments_Duration", "\"EndsAtUtc\" > \"StartsAtUtc\""));
        b.HasIndex(x => x.BookingId).HasDatabaseName("IX_BookingPriceSegments_BookingId");
    }
}

internal sealed class MarkerMapping : IEntityTypeConfiguration<InitializationMarker>
{
    public void Configure(EntityTypeBuilder<InitializationMarker> b)
    {
        b.ToTable("InitializationMarkers");
        b.HasKey(x => x.Key).HasName("PK_InitializationMarkers");
        b.Property(x => x.Key).HasColumnType("text").ValueGeneratedNever();
        ColumnRules.Text(b, "InitializationMarkers", nameof(InitializationMarker.Key));
        ColumnRules.Instant(b, "InitializationMarkers", nameof(InitializationMarker.CompletedAtUtc));
    }
}
