using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Conductor.Migrations
{
    /// <inheritdoc />
    public partial class Migration001 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DESTINATIONS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DbType = table.Column<string>(type: "text", nullable: false),
                    ConnectionString = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DESTINATIONS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ORIGINS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DbType = table.Column<string>(type: "text", nullable: false),
                    ConnectionString = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ORIGINS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RECORDS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostName = table.Column<string>(type: "text", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    CallerMethod = table.Column<string>(type: "text", nullable: false),
                    Event = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RECORDS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SCHEDULES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<bool>(type: "boolean", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SCHEDULES", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "USERS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Password = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USERS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EXTRACTIONS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ScheduleId = table.Column<long>(type: "bigint", nullable: false),
                    OriginId = table.Column<long>(type: "bigint", nullable: false),
                    DestinationId = table.Column<long>(type: "bigint", nullable: false),
                    IndexName = table.Column<string>(type: "text", nullable: false),
                    IsIncremental = table.Column<bool>(type: "boolean", nullable: false),
                    IsVirtual = table.Column<bool>(type: "boolean", nullable: false),
                    BeforeExecutionDeletes = table.Column<bool>(type: "boolean", nullable: false),
                    SingleExecution = table.Column<bool>(type: "boolean", nullable: false),
                    FilterColumn = table.Column<string>(type: "text", nullable: true),
                    FilterTime = table.Column<int>(type: "integer", nullable: true),
                    Alias = table.Column<string>(type: "text", nullable: true),
                    Dependencies = table.Column<string>(type: "text", nullable: true),
                    HttpMethod = table.Column<string>(type: "text", nullable: true),
                    HeaderStructure = table.Column<string>(type: "text", nullable: true),
                    EndpointFullName = table.Column<string>(type: "text", nullable: true),
                    BodyStructure = table.Column<string>(type: "text", nullable: true),
                    OffsetAttr = table.Column<string>(type: "text", nullable: true),
                    OffsetLimitAttr = table.Column<string>(type: "text", nullable: true),
                    PageAttr = table.Column<string>(type: "text", nullable: true),
                    TotalPageAttr = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EXTRACTIONS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EXTRACTIONS_DESTINATIONS_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "DESTINATIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EXTRACTIONS_ORIGINS_OriginId",
                        column: x => x.OriginId,
                        principalTable: "ORIGINS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EXTRACTIONS_SCHEDULES_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "SCHEDULES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EXTRACTIONS_DestinationId",
                table: "EXTRACTIONS",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_EXTRACTIONS_OriginId",
                table: "EXTRACTIONS",
                column: "OriginId");

            migrationBuilder.CreateIndex(
                name: "IX_EXTRACTIONS_ScheduleId",
                table: "EXTRACTIONS",
                column: "ScheduleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EXTRACTIONS");

            migrationBuilder.DropTable(
                name: "RECORDS");

            migrationBuilder.DropTable(
                name: "USERS");

            migrationBuilder.DropTable(
                name: "DESTINATIONS");

            migrationBuilder.DropTable(
                name: "ORIGINS");

            migrationBuilder.DropTable(
                name: "SCHEDULES");
        }
    }
}
