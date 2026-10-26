using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Devices",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Devices");
        }
    }
}
