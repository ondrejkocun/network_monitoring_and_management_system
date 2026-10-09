using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Pid = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessRuns_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActiveConnections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Protocol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LocalAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LocalPort = table.Column<int>(type: "integer", nullable: false),
                    RemoteAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RemotePort = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProcessRunId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActiveConnections_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActiveConnections_ProcessRuns_ProcessRunId",
                        column: x => x.ProcessRunId,
                        principalTable: "ProcessRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ListeningPorts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Protocol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LocalAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    ProcessRunId = table.Column<long>(type: "bigint", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningPorts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningPorts_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ListeningPorts_ProcessRuns_ProcessRunId",
                        column: x => x.ProcessRunId,
                        principalTable: "ProcessRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProcessUsages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProcessRunId = table.Column<long>(type: "bigint", nullable: false),
                    CpuUsagePercent = table.Column<double>(type: "double precision", nullable: true),
                    MemoryBytes = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessUsages_DeviceSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "DeviceSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcessUsages_ProcessRuns_ProcessRunId",
                        column: x => x.ProcessRunId,
                        principalTable: "ProcessRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveConnections_DeviceId",
                table: "ActiveConnections",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveConnections_ProcessRunId",
                table: "ActiveConnections",
                column: "ProcessRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningPorts_DeviceId_ClosedAt",
                table: "ListeningPorts",
                columns: new[] { "DeviceId", "ClosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningPorts_ProcessRunId",
                table: "ListeningPorts",
                column: "ProcessRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessRuns_DeviceId_EndedAt",
                table: "ProcessRuns",
                columns: new[] { "DeviceId", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessUsages_ProcessRunId",
                table: "ProcessUsages",
                column: "ProcessRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessUsages_SnapshotId",
                table: "ProcessUsages",
                column: "SnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveConnections");

            migrationBuilder.DropTable(
                name: "ListeningPorts");

            migrationBuilder.DropTable(
                name: "ProcessUsages");

            migrationBuilder.DropTable(
                name: "ProcessRuns");
        }
    }
}
