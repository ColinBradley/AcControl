using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcControl.Server.Migrations
{
    /// <inheritdoc />
    public partial class New9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Rco2",
                table: "AirGradientSensorEntries",
                newName: "RCo2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RCo2",
                table: "AirGradientSensorEntries",
                newName: "Rco2");
        }
    }
}
