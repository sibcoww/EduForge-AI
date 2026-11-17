using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContentAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentAccesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Visibility = table.Column<string>(type: "text", nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Snapshot = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentAccesses_Teachers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentAccesses_Kind_SourceId",
                table: "ContentAccesses",
                columns: new[] { "Kind", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentAccesses_TeacherId",
                table: "ContentAccesses",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentAccesses_Token",
                table: "ContentAccesses",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentAccesses");
        }
    }
}
