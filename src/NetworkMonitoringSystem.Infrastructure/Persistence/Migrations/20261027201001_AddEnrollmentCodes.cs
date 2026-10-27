using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnrollmentCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UsedByHostName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnrollmentCodes_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentCodes_CodeHash",
                table: "EnrollmentCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentCodes_CreatedAt",
                table: "EnrollmentCodes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentCodes_DeviceId",
                table: "EnrollmentCodes",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnrollmentCodes");
        }
    }
}
