using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ALNoor.Api.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClassSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndReason = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassSessions", x => x.Id);
                    table.CheckConstraint("CK_ClassSessions_EndReason", "\"EndReason\" IS NULL OR \"EndReason\" IN ('Teacher','Timeout','System')");
                    table.CheckConstraint("CK_ClassSessions_Status", "\"Status\" IN ('Live','Ended')");
                    table.ForeignKey(
                        name: "FK_ClassSessions_Classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassSessions_Users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionParticipantBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionParticipantBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionParticipantBlocks_ClassSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ClassSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionParticipantBlocks_Users_BlockedByUserId",
                        column: x => x.BlockedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionParticipantBlocks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_ClassId_Live",
                table: "ClassSessions",
                column: "ClassId",
                unique: true,
                filter: "\"Status\" = 'Live'");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_ClassId_StartedAt",
                table: "ClassSessions",
                columns: new[] { "ClassId", "StartedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_RoomName",
                table: "ClassSessions",
                column: "RoomName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_StartedByUserId",
                table: "ClassSessions",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionParticipantBlocks_BlockedByUserId",
                table: "SessionParticipantBlocks",
                column: "BlockedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionParticipantBlocks_SessionId_UserId",
                table: "SessionParticipantBlocks",
                columns: new[] { "SessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionParticipantBlocks_UserId",
                table: "SessionParticipantBlocks",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionParticipantBlocks");

            migrationBuilder.DropTable(
                name: "ClassSessions");
        }
    }
}
