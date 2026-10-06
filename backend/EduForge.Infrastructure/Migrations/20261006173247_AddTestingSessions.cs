using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTestingSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GroupName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<long>(type: "bigint", nullable: true),
                    Deadline = table.Column<long>(type: "bigint", nullable: true),
                    FinishedAt = table.Column<long>(type: "bigint", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Teachers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Code",
                table: "Sessions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Status_Deadline",
                table: "Sessions",
                columns: new[] { "Status", "Deadline" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_TeacherId_CreatedAt",
                table: "Sessions",
                columns: new[] { "TeacherId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
