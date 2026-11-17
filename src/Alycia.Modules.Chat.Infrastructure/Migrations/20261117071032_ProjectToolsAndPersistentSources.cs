using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alycia.Modules.Chat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProjectToolsAndPersistentSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ToolTraceJson",
                schema: "chat",
                table: "messages",
                type: "text",
                nullable: false,
                defaultValue: "[]");
            migrationBuilder.Sql("""
                UPDATE chat.attachments SET "ContextMode" = 'project'
                WHERE "ContextMode" = 'window' AND ("ArchiveId" IS NOT NULL OR POSITION('/' IN "Name") > 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE chat.attachments SET \"ContextMode\" = 'window' WHERE \"ContextMode\" = 'project';");
            migrationBuilder.DropColumn(
                name: "ToolTraceJson",
                schema: "chat",
                table: "messages");
        }
    }
}
