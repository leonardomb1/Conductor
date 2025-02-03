using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conductor.Migrations
{
    /// <inheritdoc />
    public partial class Migration002 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OverrideQuery",
                table: "EXTRACTIONS",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OverrideQuery",
                table: "EXTRACTIONS");
        }
    }
}
