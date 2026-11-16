using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alycia.Modules.Chat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentAnalysesAndSourceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetadataSource",
                schema: "chat",
                table: "attachments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "SourceCreatedAt",
                schema: "chat",
                table: "attachments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceModifiedAt",
                schema: "chat",
                table: "attachments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "document_analyses",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TreeText = table.Column<string>(type: "text", nullable: false),
                    SummaryText = table.Column<string>(type: "text", nullable: false),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    FileCount = table.Column<int>(type: "integer", nullable: false),
                    SummaryCalls = table.Column<int>(type: "integer", nullable: false),
                    UseInContext = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_analyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_analyses_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "chat",
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_analyses_ConversationId_CreatedAtUtc",
                schema: "chat",
                table: "document_analyses",
                columns: new[] { "ConversationId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_analyses",
                schema: "chat");

            migrationBuilder.DropColumn(
                name: "MetadataSource",
                schema: "chat",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "SourceCreatedAt",
                schema: "chat",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "SourceModifiedAt",
                schema: "chat",
                table: "attachments");
        }
    }
}
