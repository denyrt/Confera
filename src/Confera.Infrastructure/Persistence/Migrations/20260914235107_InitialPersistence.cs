using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Confera.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stable schema functions precede generated columns and CHECK constraints.
            migrationBuilder.Sql("""
                CREATE FUNCTION confera_name_key(value text) RETURNS text
                LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
                RETURN upper(btrim(value, U&'\0009\000A\000B\000C\000D\0020\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000') COLLATE "pg_c_utf8") COLLATE "C";
                CREATE FUNCTION confera_utf16_length(value text) RETURNS bigint
                LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
                RETURN (SELECT coalesce(sum(CASE WHEN ascii(c) > 65535 THEN 2 ELSE 1 END), 0)
                        FROM regexp_split_to_table(value, '') AS c);
                """);
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "BookingPricingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    StartsAt = table.Column<TimeOnly>(type: "time(6) without time zone", nullable: false),
                    EndsAt = table.Column<TimeOnly>(type: "time(6) without time zone", nullable: false),
                    Multiplier = table.Column<decimal>(type: "numeric", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPricingRules", x => x.Id);
                    table.UniqueConstraint("UQ_BookingPricingRules_Priority", x => x.Priority);
                    table.CheckConstraint("CK_BookingPricingRules_Code", "confera_utf16_length(\"Code\") <= 64 AND confera_name_key(\"Code\") <> ''");
                    table.CheckConstraint("CK_BookingPricingRules_DailyInterval", "\"StartsAt\" >= TIME '00:00' AND \"StartsAt\" < TIME '24:00' AND \"EndsAt\" >= TIME '00:00' AND \"EndsAt\" < TIME '24:00' AND \"StartsAt\" <> \"EndsAt\"");
                    table.CheckConstraint("CK_BookingPricingRules_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_BookingPricingRules_Multiplier", "\"Multiplier\" BETWEEN 0.50 AND 2.00 AND \"Multiplier\" = round(\"Multiplier\", 2)");
                    table.CheckConstraint("CK_BookingPricingRules_Name", "confera_utf16_length(\"Name\") <= 64 AND confera_name_key(\"Name\") <> ''");
                });

            migrationBuilder.CreateTable(
                name: "InitializationMarkers",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp(6) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitializationMarkers", x => x.Key);
                    table.CheckConstraint("CK_InitializationMarkers_CompletedAtUtc", "isfinite(\"CompletedAtUtc\") AND \"CompletedAtUtc\" BETWEEN TIMESTAMPTZ '0001-01-01 00:00:00+00' AND TIMESTAMPTZ '9999-12-31 23:59:59.999999+00'");
                    table.CheckConstraint("CK_InitializationMarkers_Key", "confera_utf16_length(\"Key\") <= 64 AND confera_name_key(\"Key\") <> ''");
                });

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    HourlyRate = table.Column<decimal>(type: "numeric", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    NameKey = table.Column<string>(type: "text", nullable: false, computedColumnSql: "confera_name_key(\"Name\")", stored: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                    table.CheckConstraint("CK_Rooms_Capacity", "\"Capacity\" > 0");
                    table.CheckConstraint("CK_Rooms_HourlyRate", "\"HourlyRate\" BETWEEN 1000 AND 100000 AND \"HourlyRate\" = round(\"HourlyRate\", 3)");
                    table.CheckConstraint("CK_Rooms_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Rooms_Name", "confera_utf16_length(\"Name\") <= 64 AND confera_name_key(\"Name\") <> ''");
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp(6) with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp(6) with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(6) with time zone", nullable: false),
                    HourlyRateSnapshot = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalPrice = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.CheckConstraint("CK_Bookings_CreatedAtUtc", "isfinite(\"CreatedAtUtc\") AND \"CreatedAtUtc\" BETWEEN TIMESTAMPTZ '0001-01-01 00:00:00+00' AND TIMESTAMPTZ '9999-12-31 23:59:59.999999+00'");
                    table.CheckConstraint("CK_Bookings_Duration", "\"EndsAtUtc\" - \"StartsAtUtc\" BETWEEN INTERVAL '30 minutes' AND INTERVAL '24 hours'");
                    table.CheckConstraint("CK_Bookings_EndsAtUtc", "isfinite(\"EndsAtUtc\") AND \"EndsAtUtc\" BETWEEN TIMESTAMPTZ '0001-01-01 00:00:00+00' AND TIMESTAMPTZ '9999-12-31 23:59:59.999999+00'");
                    table.CheckConstraint("CK_Bookings_HourlyRateSnapshot", "\"HourlyRateSnapshot\" BETWEEN 1000 AND 100000 AND \"HourlyRateSnapshot\" = round(\"HourlyRateSnapshot\", 3)");
                    table.CheckConstraint("CK_Bookings_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Bookings_StartsAtUtc", "isfinite(\"StartsAtUtc\") AND \"StartsAtUtc\" BETWEEN TIMESTAMPTZ '0001-01-01 00:00:00+00' AND TIMESTAMPTZ '9999-12-31 23:59:59.999999+00'");
                    table.CheckConstraint("CK_Bookings_TotalPrice", "\"TotalPrice\" BETWEEN 0 AND 999999999999999.999 AND \"TotalPrice\" = round(\"TotalPrice\", 3)");
                    table.ForeignKey(
                        name: "FK_Bookings_Rooms",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoomServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    NameKey = table.Column<string>(type: "text", nullable: false, computedColumnSql: "confera_name_key(\"Name\")", stored: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomServices", x => x.Id);
                    table.CheckConstraint("CK_RoomServices_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_RoomServices_Name", "confera_utf16_length(\"Name\") <= 64 AND confera_name_key(\"Name\") <> ''");
                    table.CheckConstraint("CK_RoomServices_Price", "\"Price\" BETWEEN 200 AND 20000 AND \"Price\" = round(\"Price\", 3)");
                    table.ForeignKey(
                        name: "FK_RoomServices_Rooms",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookedRoomServiceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    ServicePriceSnapshot = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookedRoomServiceSnapshots", x => x.Id);
                    table.CheckConstraint("CK_BookedRoomServiceSnapshots_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_BookedRoomServiceSnapshots_ServiceNameSnapshot", "confera_utf16_length(\"ServiceNameSnapshot\") <= 64 AND confera_name_key(\"ServiceNameSnapshot\") <> ''");
                    table.CheckConstraint("CK_BookedRoomServiceSnapshots_ServicePriceSnapshot", "\"ServicePriceSnapshot\" BETWEEN 200 AND 20000 AND \"ServicePriceSnapshot\" = round(\"ServicePriceSnapshot\", 3)");
                    table.ForeignKey(
                        name: "FK_BookedRoomServiceSnapshots_Bookings",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingPriceSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp(6) with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp(6) with time zone", nullable: false),
                    PricingCodeSnapshot = table.Column<string>(type: "text", nullable: false),
                    MultiplierSnapshot = table.Column<decimal>(type: "numeric", nullable: false),
                    HourlyRateSnapshot = table.Column<decimal>(type: "numeric", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPriceSegments", x => x.Id);
                    table.CheckConstraint("CK_BookingPriceSegments_Duration", "\"EndsAtUtc\" > \"StartsAtUtc\"");
                    table.CheckConstraint("CK_BookingPriceSegments_EndsAtUtc", "isfinite(\"EndsAtUtc\") AND \"EndsAtUtc\" BETWEEN TIMESTAMPTZ '0001-01-01 00:00:00+00' AND TIMESTAMPTZ '9999-12-31 23:59:59.999999+00'");
                    table.CheckConstraint("CK_BookingPriceSegments_HourlyRateSnapshot", "\"HourlyRateSnapshot\" BETWEEN 1000 AND 100000 AND \"HourlyRateSnapshot\" = round(\"HourlyRateSnapshot\", 3)");
                    table.CheckConstraint("CK_BookingPriceSegments_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_BookingPriceSegments_MultiplierSnapshot", "\"MultiplierSnapshot\" BETWEEN 0.50 AND 2.00 AND \"MultiplierSnapshot\" = round(\"MultiplierSnapshot\", 2)");
                    table.CheckConstraint("CK_BookingPriceSegments_Price", "\"Price\" BETWEEN 0 AND 4800000 AND \"Price\" = round(\"Price\", 3)");
                    table.CheckConstraint("CK_BookingPriceSegments_PricingCodeSnapshot", "confera_utf16_length(\"PricingCodeSnapshot\") <= 64 AND confera_name_key(\"PricingCodeSnapshot\") <> ''");
                    table.CheckConstraint("CK_BookingPriceSegments_StartsAtUtc", "isfinite(\"StartsAtUtc\") AND \"StartsAtUtc\" BETWEEN TIMESTAMPTZ '0001-01-01 00:00:00+00' AND TIMESTAMPTZ '9999-12-31 23:59:59.999999+00'");
                    table.ForeignKey(
                        name: "FK_BookingPriceSegments_Bookings",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookedRoomServiceSnapshots_BookingId",
                table: "BookedRoomServiceSnapshots",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingPriceSegments_BookingId",
                table: "BookingPriceSegments",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_RoomPeriod",
                table: "Bookings",
                columns: new[] { "RoomId", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Rooms_ActiveName",
                table: "Rooms",
                column: "NameKey",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_RoomServices_RoomName",
                table: "RoomServices",
                columns: new[] { "RoomId", "NameKey" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE "Bookings" ADD CONSTRAINT "EX_Bookings_RoomPeriod"
                EXCLUDE USING gist ("RoomId" WITH =, tstzrange("StartsAtUtc", "EndsAtUtc", '[)') WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookedRoomServiceSnapshots");

            migrationBuilder.DropTable(
                name: "BookingPriceSegments");

            migrationBuilder.DropTable(
                name: "BookingPricingRules");

            migrationBuilder.DropTable(
                name: "InitializationMarkers");

            migrationBuilder.DropTable(
                name: "RoomServices");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.Sql("DROP FUNCTION confera_name_key(text); DROP FUNCTION confera_utf16_length(text);");
        }
    }
}
