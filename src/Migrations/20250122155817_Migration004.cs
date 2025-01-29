using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conductor.Migrations
{
    /// <inheritdoc />
    public partial class Migration004 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VirtualId",
                table: "EXTRACTIONS",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VirtualId",
                table: "EXTRACTIONS");
        }
    }
}
