using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Walletter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLastExecutedAtToRecurring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastExecutedAt",
                table: "recurring_payments",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastExecutedAt",
                table: "recurring_payments");
        }
    }
}
