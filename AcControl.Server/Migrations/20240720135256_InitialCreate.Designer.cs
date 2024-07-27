﻿// <auto-generated />
using System;
using AcControl.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace AcControl.Server.Migrations
{
    [DbContext(typeof(HomeDbContext))]
    [Migration("20240720135256_InitialCreate")]
    partial class InitialCreate
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "8.0.7");

            modelBuilder.Entity("AcControl.Server.Data.Models.AirGradientSensorEntry", b =>
                {
                    b.Property<string>("SerialNumber")
                        .HasColumnType("TEXT");

                    b.Property<DateTimeOffset>("DateTime")
                        .HasColumnType("TEXT");

                    b.Property<double>("Atmp")
                        .HasColumnType("REAL");

                    b.Property<double>("AtmpCompensated")
                        .HasColumnType("REAL");

                    b.Property<int>("NoxIndex")
                        .HasColumnType("INTEGER");

                    b.Property<int>("NoxRaw")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Pm003Count")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Pm01")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Pm02")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Pm10")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Rco2")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Rhum")
                        .HasColumnType("INTEGER");

                    b.Property<int>("RhumCompensated")
                        .HasColumnType("INTEGER");

                    b.Property<int>("TvocIndex")
                        .HasColumnType("INTEGER");

                    b.Property<int>("TvocRaw")
                        .HasColumnType("INTEGER");

                    b.Property<int>("WiFiStrength")
                        .HasColumnType("INTEGER");

                    b.HasKey("SerialNumber", "DateTime");

                    b.HasIndex("SerialNumber", "DateTime")
                        .IsUnique();

                    b.ToTable("AirGradientSensorEntries");
                });

            modelBuilder.Entity("AcControl.Server.Data.Models.InverterDaySummaryEntry", b =>
                {
                    b.Property<DateOnly>("Date")
                        .HasColumnType("TEXT");

                    b.Property<string>("Entries")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Date");

                    b.ToTable("InverterDaySummaries");
                });
#pragma warning restore 612, 618
        }
    }
}
