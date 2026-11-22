using Microsoft.EntityFrameworkCore.Migrations;

namespace Alycia.Modules.Chat.Infrastructure.Migrations;

public partial class BorealProjects : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE chat.workspace_profiles (
            "Id" uuid CONSTRAINT "PK_workspace_profiles" PRIMARY KEY,
            "Instructions" varchar(4000) NOT NULL, "DefaultModel" varchar(200) NOT NULL,
            "NotesJson" text NOT NULL, "Archived" boolean NOT NULL, "Revision" integer NOT NULL
        );
        ALTER TABLE chat.conversations ADD COLUMN "WorkspaceId" uuid NULL
            CONSTRAINT "FK_conversations_workspace_profiles_WorkspaceId" REFERENCES chat.workspace_profiles("Id") ON DELETE RESTRICT;
        ALTER TABLE chat.attachments ALTER COLUMN "ConversationId" DROP NOT NULL;
        ALTER TABLE chat.attachments ADD COLUMN "WorkspaceId" uuid NULL
            CONSTRAINT "FK_attachments_workspace_profiles_WorkspaceId" REFERENCES chat.workspace_profiles("Id") ON DELETE RESTRICT;
        ALTER TABLE chat.attachments ADD CONSTRAINT ck_attachment_owner CHECK (num_nonnulls("ConversationId","WorkspaceId") = 1);
        CREATE INDEX "IX_attachments_WorkspaceId" ON chat.attachments ("WorkspaceId");
        CREATE INDEX "IX_conversations_WorkspaceId" ON chat.conversations ("WorkspaceId");
        CREATE TABLE chat.workspace_file_overrides (
            "ConversationId" uuid NOT NULL CONSTRAINT "FK_workspace_file_overrides_conversations_ConversationId" REFERENCES chat.conversations("Id") ON DELETE CASCADE,
            "FileId" uuid NOT NULL CONSTRAINT "FK_workspace_file_overrides_attachments_FileId" REFERENCES chat.attachments("Id") ON DELETE CASCADE,
            "Mode" varchar(16) NOT NULL, CONSTRAINT "PK_workspace_file_overrides" PRIMARY KEY ("ConversationId","FileId")
        );
        CREATE INDEX "IX_workspace_file_overrides_FileId" ON chat.workspace_file_overrides ("FileId");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $guard$ BEGIN
            IF EXISTS (SELECT 1 FROM chat.workspace_profiles) THEN
                RAISE EXCEPTION 'Des projets existent. Pour revenir à la version précédente, restaurez la sauvegarde.';
            END IF;
        END $guard$;
        DROP TABLE chat.workspace_file_overrides;
        ALTER TABLE chat.attachments DROP CONSTRAINT ck_attachment_owner, DROP COLUMN "WorkspaceId";
        ALTER TABLE chat.attachments ALTER COLUMN "ConversationId" SET NOT NULL;
        ALTER TABLE chat.conversations DROP COLUMN "WorkspaceId";
        DROP TABLE chat.workspace_profiles;
        """);
}
