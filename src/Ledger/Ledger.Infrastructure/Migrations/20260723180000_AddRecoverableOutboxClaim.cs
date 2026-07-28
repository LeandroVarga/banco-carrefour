using System;
using BancoCarrefour.Contracts.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BancoCarrefour.Ledger.Infrastructure.Migrations
{
    /// <inheritdoc />
    [MigrationPhase(MigrationPhase.Expand)]
    public partial class AddRecoverableOutboxClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "locked_by",
                table: "outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_status_next_attempt_at_created_at",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_status_next_attempt_at_created_at",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "locked_by",
                table: "outbox_messages");
        }
    }
}
