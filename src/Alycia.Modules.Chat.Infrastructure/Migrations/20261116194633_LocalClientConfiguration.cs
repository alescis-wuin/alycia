using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alycia.Modules.Chat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LocalClientConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContextNotes",
                schema: "chat",
                table: "conversations",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SettingsJson",
                schema: "chat",
                table: "conversations",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "ContextMode",
                schema: "chat",
                table: "attachments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "window");

            migrationBuilder.CreateTable(
                name: "prompt_profiles",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SystemPrompt = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_profiles", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prompt_profiles",
                schema: "chat");

            migrationBuilder.DropColumn(
                name: "ContextNotes",
                schema: "chat",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "SettingsJson",
                schema: "chat",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "ContextMode",
                schema: "chat",
                table: "attachments");
        }
    }
}
