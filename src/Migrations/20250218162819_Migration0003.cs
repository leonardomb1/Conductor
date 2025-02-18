using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conductor.Migrations
{
    /// <inheritdoc />
    public partial class Migration0003 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TimeZoneOffSet",
                table: "ORIGINS",
                type: "integer",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AddColumn<string>(
                name: "FilterCondition",
                table: "EXTRACTIONS",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TimeZoneOffSet",
                table: "DESTINATIONS",
                type: "integer",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "double precision");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilterCondition",
                table: "EXTRACTIONS");

            migrationBuilder.AlterColumn<double>(
                name: "TimeZoneOffSet",
                table: "ORIGINS",
                type: "double precision",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<double>(
                name: "TimeZoneOffSet",
                table: "DESTINATIONS",
                type: "double precision",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");
        }
    }
}
