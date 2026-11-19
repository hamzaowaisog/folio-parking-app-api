using System;
using App.Api;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace App.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:account_role", "employee,gateGuard")
                .Annotation("Npgsql:Enum:employee_parking_status", "Dedicated,Temporary")
                .Annotation("Npgsql:Enum:parking_claim_status", "Claimed")
                .Annotation("Npgsql:Enum:parking_release_status", "Reserved,Released,Claimed")
                .Annotation("Npgsql:Enum:waitlist_entry_status", "Waiting,Offered,Claimed,Expired,Left")
                .OldAnnotation("Npgsql:Enum:account_role", "employee,gateGuard")
                .OldAnnotation("Npgsql:Enum:employee_parking_status", "Dedicated,Temporary")
                .OldAnnotation("Npgsql:Enum:parking_claim_status", "Claimed")
                .OldAnnotation("Npgsql:Enum:parking_release_status", "Reserved,Released,Claimed");

            migrationBuilder.CreateTable(
                name: "WaitlistEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<WaitlistEntryStatus>(type: "waitlist_entry_status", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleaseId = table.Column<int>(type: "integer", nullable: true),
                    OfferedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HeldUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitlistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_ParkingReleases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "ParkingReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_Date_Status",
                table: "WaitlistEntries",
                columns: new[] { "Date", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_EmployeeId_Date",
                table: "WaitlistEntries",
                columns: new[] { "EmployeeId", "Date" },
                unique: true,
                filter: "\"Status\" IN ('Waiting', 'Offered')");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_ReleaseId",
                table: "WaitlistEntries",
                column: "ReleaseId",
                unique: true,
                filter: "\"Status\" = 'Offered'");

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_cast WHERE castsource = 'text'::regtype AND casttarget = 'waitlist_entry_status'::regtype) THEN
                        EXECUTE 'CREATE CAST (text AS waitlist_entry_status) WITH INOUT AS IMPLICIT';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WaitlistEntries");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:account_role", "employee,gateGuard")
                .Annotation("Npgsql:Enum:employee_parking_status", "Dedicated,Temporary")
                .Annotation("Npgsql:Enum:parking_claim_status", "Claimed")
                .Annotation("Npgsql:Enum:parking_release_status", "Reserved,Released,Claimed")
                .OldAnnotation("Npgsql:Enum:account_role", "employee,gateGuard")
                .OldAnnotation("Npgsql:Enum:employee_parking_status", "Dedicated,Temporary")
                .OldAnnotation("Npgsql:Enum:parking_claim_status", "Claimed")
                .OldAnnotation("Npgsql:Enum:parking_release_status", "Reserved,Released,Claimed")
                .OldAnnotation("Npgsql:Enum:waitlist_entry_status", "Waiting,Offered,Claimed,Expired,Left");
        }
    }
}
