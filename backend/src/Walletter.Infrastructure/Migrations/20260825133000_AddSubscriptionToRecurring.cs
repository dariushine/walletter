using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Walletter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionToRecurring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSubscription",
                table: "recurring_payments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BillingDay",
                table: "recurring_payments",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingDay",
                table: "recurring_payments");

            migrationBuilder.DropColumn(
                name: "IsSubscription",
                table: "recurring_payments");
        }
    }
}