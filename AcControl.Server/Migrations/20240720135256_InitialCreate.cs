using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcControl.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AirGradientSensorEntries",
                columns: table => new
                {
                    DateTime = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", nullable: false),
                    WiFiStrength = table.Column<int>(type: "INTEGER", nullable: false),
                    Rco2 = table.Column<int>(type: "INTEGER", nullable: false),
                    Pm01 = table.Column<int>(type: "INTEGER", nullable: false),
                    Pm02 = table.Column<int>(type: "INTEGER", nullable: false),
                    Pm10 = table.Column<int>(type: "INTEGER", nullable: false),
                    Pm003Count = table.Column<int>(type: "INTEGER", nullable: false),
                    Atmp = table.Column<double>(type: "REAL", nullable: false),
                    AtmpCompensated = table.Column<double>(type: "REAL", nullable: false),
                    Rhum = table.Column<int>(type: "INTEGER", nullable: false),
                    RhumCompensated = table.Column<int>(type: "INTEGER", nullable: false),
                    TvocIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    TvocRaw = table.Column<int>(type: "INTEGER", nullable: false),
                    NoxIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    NoxRaw = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AirGradientSensorEntries", x => new { x.SerialNumber, x.DateTime });
                });

            migrationBuilder.CreateTable(
                name: "InverterDaySummaries",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Entries = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InverterDaySummaries", x => x.Date);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AirGradientSensorEntries_SerialNumber_DateTime",
                table: "AirGradientSensorEntries",
                columns: new[] { "SerialNumber", "DateTime" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AirGradientSensorEntries");

            migrationBuilder.DropTable(
                name: "InverterDaySummaries");
        }
    }
}
