using System;
using Microsoft.EntityFrameworkCore.Migrations;

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
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DbType = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionString = table.Column<string>(type: "TEXT", nullable: false),
                    TimeZoneOffSet = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DESTINATIONS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JOBS",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobGuid = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobType = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ExtractionIds = table.Column<string>(type: "TEXT", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BytesAccumulated = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JOBS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ORIGINS",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", nullable: true),
                    DbType = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionString = table.Column<string>(type: "TEXT", nullable: false),
                    TimeZoneOffSet = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ORIGINS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RECORDS",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostName = table.Column<string>(type: "TEXT", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    CallerMethod = table.Column<string>(type: "TEXT", nullable: false),
                    Event = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RECORDS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SCHEDULES",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<bool>(type: "INTEGER", nullable: false),
                    Value = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SCHEDULES", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "USERS",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Password = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USERS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EXTRACTIONS",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ScheduleId = table.Column<uint>(type: "INTEGER", nullable: true),
                    OriginId = table.Column<uint>(type: "INTEGER", nullable: false),
                    DestinationId = table.Column<uint>(type: "INTEGER", nullable: true),
                    IndexName = table.Column<string>(type: "TEXT", nullable: false),
                    IsIncremental = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsVirtual = table.Column<bool>(type: "INTEGER", nullable: false),
                    VirtualId = table.Column<string>(type: "TEXT", nullable: true),
                    VirtualIdGroup = table.Column<string>(type: "TEXT", nullable: true),
                    IsVirtualTemplate = table.Column<bool>(type: "INTEGER", nullable: true),
                    SingleExecution = table.Column<bool>(type: "INTEGER", nullable: false),
                    FilterColumn = table.Column<string>(type: "TEXT", nullable: true),
                    FilterTime = table.Column<int>(type: "INTEGER", nullable: true),
                    OverrideQuery = table.Column<string>(type: "TEXT", nullable: true),
                    Alias = table.Column<string>(type: "TEXT", nullable: true),
                    Dependencies = table.Column<string>(type: "TEXT", nullable: true),
                    HttpMethod = table.Column<string>(type: "TEXT", nullable: true),
                    HeaderStructure = table.Column<string>(type: "TEXT", nullable: true),
                    EndpointFullName = table.Column<string>(type: "TEXT", nullable: true),
                    BodyStructure = table.Column<string>(type: "TEXT", nullable: true),
                    OffsetAttr = table.Column<string>(type: "TEXT", nullable: true),
                    OffsetLimitAttr = table.Column<string>(type: "TEXT", nullable: true),
                    PageAttr = table.Column<string>(type: "TEXT", nullable: true),
                    TotalPageAttr = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EXTRACTIONS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EXTRACTIONS_DESTINATIONS_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "DESTINATIONS",
                        principalColumn: "Id");
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
                        principalColumn: "Id");
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
                name: "JOBS");

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
