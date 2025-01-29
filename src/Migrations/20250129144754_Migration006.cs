using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conductor.Migrations
{
    /// <inheritdoc />
    public partial class Migration006 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "TimeZoneOffSet",
                table: "ORIGINS",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TimeZoneOffSet",
                table: "DESTINATIONS",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZoneOffSet",
                table: "ORIGINS");

            migrationBuilder.DropColumn(
                name: "TimeZoneOffSet",
                table: "DESTINATIONS");
        }
    }
}
