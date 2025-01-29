using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conductor.Migrations
{
    /// <inheritdoc />
    public partial class Migration007 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VirtualIdGroup",
                table: "EXTRACTIONS",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VirtualIdGroup",
                table: "EXTRACTIONS");
        }
    }
}
