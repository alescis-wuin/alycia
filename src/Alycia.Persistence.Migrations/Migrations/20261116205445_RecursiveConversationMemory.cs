using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alycia.Modules.Chat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecursiveConversationMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MemoryModel",
                schema: "chat",
                table: "conversations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MemorySourceHash",
                schema: "chat",
                table: "conversations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MemoryText",
                schema: "chat",
                table: "conversations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MemoryThroughSequence",
                schema: "chat",
                table: "conversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MemoryModel",
                schema: "chat",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "MemorySourceHash",
                schema: "chat",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "MemoryText",
                schema: "chat",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "MemoryThroughSequence",
                schema: "chat",
                table: "conversations");
        }
    }
}
