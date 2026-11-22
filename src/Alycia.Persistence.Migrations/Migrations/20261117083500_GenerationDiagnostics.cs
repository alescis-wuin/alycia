using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alycia.Modules.Chat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GenerationDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiagnosticsJson",
                schema: "chat",
                table: "messages",
                type: "text",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiagnosticsJson",
                schema: "chat",
                table: "messages");
        }
    }
}
