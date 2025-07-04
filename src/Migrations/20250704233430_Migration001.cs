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
                    ConnectionString = table.Column<string>(type: "text", nullable: false),
                    TimeZoneOffSet = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DESTINATIONS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JOBS",
                columns: table => new
                {
                    JobGuid = table.Column<Guid>(type: "uuid", nullable: false),
                    JobType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JOBS", x => x.JobGuid);
                });

            migrationBuilder.CreateTable(
                name: "ORIGINS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: true),
                    DbType = table.Column<string>(type: "text", nullable: true),
                    ConnectionString = table.Column<string>(type: "text", nullable: true),
                    TimeZoneOffSet = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ORIGINS", x => x.Id);
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
                    ScheduleId = table.Column<long>(type: "bigint", nullable: true),
                    OriginId = table.Column<long>(type: "bigint", nullable: false),
                    DestinationId = table.Column<long>(type: "bigint", nullable: true),
                    IndexName = table.Column<string>(type: "text", nullable: true),
                    IsIncremental = table.Column<bool>(type: "boolean", nullable: false),
                    IsVirtual = table.Column<bool>(type: "boolean", nullable: false),
                    VirtualId = table.Column<string>(type: "text", nullable: true),
                    VirtualIdGroup = table.Column<string>(type: "text", nullable: true),
                    IsVirtualTemplate = table.Column<bool>(type: "boolean", nullable: true),
                    FilterCondition = table.Column<string>(type: "text", nullable: true),
                    FilterColumn = table.Column<string>(type: "text", nullable: true),
                    FilterTime = table.Column<int>(type: "integer", nullable: true),
                    OverrideQuery = table.Column<string>(type: "text", nullable: true),
                    Alias = table.Column<string>(type: "text", nullable: true),
                    Dependencies = table.Column<string>(type: "text", nullable: true),
                    IgnoreColumns = table.Column<string>(type: "text", nullable: true),
                    HttpMethod = table.Column<string>(type: "text", nullable: true),
                    HeaderStructure = table.Column<string>(type: "text", nullable: true),
                    EndpointFullName = table.Column<string>(type: "text", nullable: true),
                    BodyStructure = table.Column<string>(type: "text", nullable: true),
                    OffsetAttr = table.Column<string>(type: "text", nullable: true),
                    OffsetLimitAttr = table.Column<string>(type: "text", nullable: true),
                    PageAttr = table.Column<string>(type: "text", nullable: true),
                    PaginationType = table.Column<string>(type: "text", nullable: true),
                    TotalPageAttr = table.Column<string>(type: "text", nullable: true),
                    SourceType = table.Column<string>(type: "text", nullable: true),
                    Script = table.Column<string>(type: "text", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "JOBS_EXTRACTIONS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobGuid = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractionId = table.Column<long>(type: "bigint", nullable: false),
                    BytesAccumulated = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JOBS_EXTRACTIONS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JOBS_EXTRACTIONS_EXTRACTIONS_ExtractionId",
                        column: x => x.ExtractionId,
                        principalTable: "EXTRACTIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JOBS_EXTRACTIONS_JOBS_JobGuid",
                        column: x => x.JobGuid,
                        principalTable: "JOBS",
                        principalColumn: "JobGuid",
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

            migrationBuilder.CreateIndex(
                name: "IX_JOBS_EXTRACTIONS_ExtractionId",
                table: "JOBS_EXTRACTIONS",
                column: "ExtractionId");

            migrationBuilder.CreateIndex(
                name: "IX_JOBS_EXTRACTIONS_JobGuid",
                table: "JOBS_EXTRACTIONS",
                column: "JobGuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JOBS_EXTRACTIONS");

            migrationBuilder.DropTable(
                name: "USERS");

            migrationBuilder.DropTable(
                name: "EXTRACTIONS");

            migrationBuilder.DropTable(
                name: "JOBS");

            migrationBuilder.DropTable(
                name: "DESTINATIONS");

            migrationBuilder.DropTable(
                name: "ORIGINS");

            migrationBuilder.DropTable(
                name: "SCHEDULES");
        }
    }
}
