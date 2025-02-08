using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conductor.Migrations
{
    /// <inheritdoc />
    public partial class Migration003 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EXTRACTIONS_DESTINATIONS_DestinationId",
                table: "EXTRACTIONS");

            migrationBuilder.AlterColumn<long>(
                name: "DestinationId",
                table: "EXTRACTIONS",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddForeignKey(
                name: "FK_EXTRACTIONS_DESTINATIONS_DestinationId",
                table: "EXTRACTIONS",
                column: "DestinationId",
                principalTable: "DESTINATIONS",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EXTRACTIONS_DESTINATIONS_DestinationId",
                table: "EXTRACTIONS");

            migrationBuilder.AlterColumn<long>(
                name: "DestinationId",
                table: "EXTRACTIONS",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EXTRACTIONS_DESTINATIONS_DestinationId",
                table: "EXTRACTIONS",
                column: "DestinationId",
                principalTable: "DESTINATIONS",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
