using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTcpPortCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReachedBy",
                table: "DeviceSnapshots",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TcpPort",
                table: "Devices",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReachedBy",
                table: "DeviceSnapshots");

            migrationBuilder.DropColumn(
                name: "TcpPort",
                table: "Devices");
        }
    }
}
