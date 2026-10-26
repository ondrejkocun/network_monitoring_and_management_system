using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrafficRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CaptureTraffic",
                table: "Devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TrafficCaptureProblem",
                table: "Devices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrafficRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Transport = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Protocol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RemoteAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ServicePort = table.Column<int>(type: "integer", nullable: true),
                    PacketsSent = table.Column<long>(type: "bigint", nullable: false),
                    PacketsReceived = table.Column<long>(type: "bigint", nullable: false),
                    BytesSent = table.Column<long>(type: "bigint", nullable: false),
                    BytesReceived = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrafficRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrafficRecords_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrafficRecords_DeviceId_PeriodEnd",
                table: "TrafficRecords",
                columns: new[] { "DeviceId", "PeriodEnd" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrafficRecords");

            migrationBuilder.DropColumn(
                name: "CaptureTraffic",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "TrafficCaptureProblem",
                table: "Devices");
        }
    }
}
