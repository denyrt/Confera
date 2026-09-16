using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Confera.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoomVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Rooms",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rooms_Version",
                table: "Rooms",
                sql: "\"Version\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Rooms_Version",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Rooms");
        }
    }
}
