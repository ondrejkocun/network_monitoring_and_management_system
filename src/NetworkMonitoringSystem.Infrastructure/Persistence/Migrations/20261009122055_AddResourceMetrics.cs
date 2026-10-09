using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CpuUsagePercent",
                table: "DeviceSnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasResources",
                table: "DeviceSnapshots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "MemoryTotalBytes",
                table: "DeviceSnapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MemoryUsedBytes",
                table: "DeviceSnapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiskSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    FreeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiskSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiskSnapshots_DeviceSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "DeviceSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NetworkInterfaceSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MacAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsUp = table.Column<bool>(type: "boolean", nullable: false),
                    BytesSent = table.Column<long>(type: "bigint", nullable: false),
                    BytesReceived = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetworkInterfaceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NetworkInterfaceSnapshots_DeviceSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "DeviceSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiskSnapshots_SnapshotId",
                table: "DiskSnapshots",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkInterfaceSnapshots_SnapshotId",
                table: "NetworkInterfaceSnapshots",
                column: "SnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiskSnapshots");

            migrationBuilder.DropTable(
                name: "NetworkInterfaceSnapshots");

            migrationBuilder.DropColumn(
                name: "CpuUsagePercent",
                table: "DeviceSnapshots");

            migrationBuilder.DropColumn(
                name: "HasResources",
                table: "DeviceSnapshots");

            migrationBuilder.DropColumn(
                name: "MemoryTotalBytes",
                table: "DeviceSnapshots");

            migrationBuilder.DropColumn(
                name: "MemoryUsedBytes",
                table: "DeviceSnapshots");
        }
    }
}
