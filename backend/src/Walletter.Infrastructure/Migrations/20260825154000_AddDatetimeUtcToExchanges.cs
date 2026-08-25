using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Walletter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDatetimeUtcToExchanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DatetimeUtc",
                table: "exchanges",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Backfill: los exchanges existentes usan su CreatedAt como fecha efectiva.
            migrationBuilder.Sql(
                "UPDATE \"exchanges\" SET \"DatetimeUtc\" = \"CreatedAt\" WHERE \"DatetimeUtc\" = '0001-01-01 00:00:00';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DatetimeUtc",
                table: "exchanges");
        }
    }
}