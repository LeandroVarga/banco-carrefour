using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BancoCarrefour.Consolidation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialConsolidationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_balances",
                columns: table => new
                {
                    daily_balance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_credits = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total_debits = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    entry_count = table.Column<long>(type: "bigint", nullable: false),
                    last_event_occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_balances", x => x.daily_balance_id);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                columns: table => new
                {
                    processed_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_version = table.Column<int>(type: "integer", nullable: false),
                    merchant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_events", x => x.processed_event_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_balances_merchant_id_business_date",
                table: "daily_balances",
                columns: new[] { "merchant_id", "business_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processed_events_event_id",
                table: "processed_events",
                column: "event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_balances");

            migrationBuilder.DropTable(
                name: "processed_events");
        }
    }
}
