﻿// <auto-generated />
using System;
using Conductor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Conductor.Migrations
{
    [DbContext(typeof(EfContext))]
    partial class EfContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.1")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Conductor.Model.Destination", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

                    b.Property<string>("ConnectionString")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasAnnotation("Relational:JsonPropertyName", "destinationConStr");

                    b.Property<string>("DbType")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasAnnotation("Relational:JsonPropertyName", "destinationDbType");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasAnnotation("Relational:JsonPropertyName", "destinationName");

                    b.Property<double>("TimeZoneOffSet")
                        .HasColumnType("double precision")
                        .HasAnnotation("Relational:JsonPropertyName", "destinationTimeZoneOffSet");

                    b.HasKey("Id");

                    b.ToTable("DESTINATIONS");
                });

            modelBuilder.Entity("Conductor.Model.Extraction", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

                    b.Property<string>("Alias")
                        .HasColumnType("text")
                        .HasAnnotation("Relational:JsonPropertyName", "extractionAlias");

                    b.Property<bool>("BeforeExecutionDeletes")
                        .HasColumnType("boolean");

                    b.Property<string>("BodyStructure")
                        .HasColumnType("text");

                    b.Property<string>("Dependencies")
                        .HasColumnType("text");

                    b.Property<long?>("DestinationId")
                        .HasColumnType("bigint");

                    b.Property<string>("EndpointFullName")
                        .HasColumnType("text");

                    b.Property<string>("FilterColumn")
                        .HasColumnType("text");

                    b.Property<int?>("FilterTime")
                        .HasColumnType("integer");

                    b.Property<string>("HeaderStructure")
                        .HasColumnType("text");

                    b.Property<string>("HttpMethod")
                        .HasColumnType("text");

                    b.Property<string>("IndexName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<bool>("IsIncremental")
                        .HasColumnType("boolean");

                    b.Property<bool>("IsVirtual")
                        .HasColumnType("boolean");

                    b.Property<bool?>("IsVirtualTemplate")
                        .HasColumnType("boolean");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasAnnotation("Relational:JsonPropertyName", "extractionName");

                    b.Property<string>("OffsetAttr")
                        .HasColumnType("text");

                    b.Property<string>("OffsetLimitAttr")
                        .HasColumnType("text");

                    b.Property<long>("OriginId")
                        .HasColumnType("bigint");

                    b.Property<string>("OverrideQuery")
                        .HasColumnType("text");

                    b.Property<string>("PageAttr")
                        .HasColumnType("text");

                    b.Property<long?>("ScheduleId")
                        .HasColumnType("bigint");

                    b.Property<bool>("SingleExecution")
                        .HasColumnType("boolean");

                    b.Property<string>("TableStructure")
                        .HasColumnType("text");

                    b.Property<string>("TotalPageAttr")
                        .HasColumnType("text");

                    b.Property<string>("VirtualId")
                        .HasColumnType("text");

                    b.Property<string>("VirtualIdGroup")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("DestinationId");

                    b.HasIndex("OriginId");

                    b.HasIndex("ScheduleId");

                    b.ToTable("EXTRACTIONS");
                });

            modelBuilder.Entity("Conductor.Model.Origin", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

                    b.Property<string>("Alias")
                        .HasColumnType("text")
                        .HasAnnotation("Relational:JsonPropertyName", "originAlias");

                    b.Property<string>("ConnectionString")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasAnnotation("Relational:JsonPropertyName", "originConStr");

                    b.Property<string>("DbType")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasAnnotation("Relational:JsonPropertyName", "originDbType");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasAnnotation("Relational:JsonPropertyName", "originName");

                    b.Property<double>("TimeZoneOffSet")
                        .HasColumnType("double precision")
                        .HasAnnotation("Relational:JsonPropertyName", "originTimeZoneOffSet");

                    b.HasKey("Id");

                    b.ToTable("ORIGINS");
                });

            modelBuilder.Entity("Conductor.Model.Record", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

                    b.Property<string>("CallerMethod")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Event")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("EventType")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("HostName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("TimeStamp")
                        .HasColumnType("timestamp with time zone");

                    b.HasKey("Id");

                    b.ToTable("RECORDS");
                });

            modelBuilder.Entity("Conductor.Model.Schedule", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasAnnotation("Relational:JsonPropertyName", "scheduleName");

                    b.Property<bool>("Status")
                        .HasColumnType("boolean");

                    b.Property<int>("Value")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.ToTable("SCHEDULES");
                });

            modelBuilder.Entity("Conductor.Model.User", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasAnnotation("Relational:JsonPropertyName", "username");

                    b.Property<string>("Password")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("USERS");
                });

            modelBuilder.Entity("Conductor.Model.Extraction", b =>
                {
                    b.HasOne("Conductor.Model.Destination", "Destination")
                        .WithMany()
                        .HasForeignKey("DestinationId");

                    b.HasOne("Conductor.Model.Origin", "Origin")
                        .WithMany()
                        .HasForeignKey("OriginId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Conductor.Model.Schedule", "Schedule")
                        .WithMany()
                        .HasForeignKey("ScheduleId");

                    b.Navigation("Destination");

                    b.Navigation("Origin");

                    b.Navigation("Schedule");
                });
#pragma warning restore 612, 618
        }
    }
}
