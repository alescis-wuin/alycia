using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alycia.Modules.Chat.Infrastructure.Migrations;

// Operational index tables are managed in SQL, independently of the domain model snapshot.
[DbContext(typeof(ChatDbContext))]
[Migration("20261118200000_PersistentEmbeddings")]
public sealed class PersistentEmbeddings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE chat.import_settings (id integer PRIMARY KEY CHECK (id = 1), settings_json text NOT NULL);
        CREATE TABLE chat.embedding_jobs (
            owner_id uuid NOT NULL, is_project boolean NOT NULL, options_json text NOT NULL,
            revision integer NOT NULL DEFAULT 0, state text NOT NULL DEFAULT 'queued',
            completed integer NOT NULL DEFAULT 0, total integer NOT NULL DEFAULT 0,
            detail text NOT NULL DEFAULT '', fingerprint text NOT NULL DEFAULT '',
            updated_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(owner_id, is_project));
        CREATE TABLE chat.embedding_vectors (
            identity text NOT NULL, key varchar(64) NOT NULL,
            file_id uuid REFERENCES chat.attachments("Id") ON DELETE CASCADE,
            conversation_id uuid REFERENCES chat.conversations("Id") ON DELETE CASCADE,
            sequence integer NOT NULL DEFAULT 0, start_offset integer NOT NULL, end_offset integer NOT NULL,
            content_hash varchar(64) NOT NULL, embedding real[] NOT NULL,
            PRIMARY KEY(identity, key), CHECK (num_nonnulls(file_id, conversation_id) = 1));
        CREATE INDEX ix_embedding_file ON chat.embedding_vectors(file_id);
        CREATE INDEX ix_embedding_history ON chat.embedding_vectors(conversation_id, sequence);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE chat.import_settings;
        DROP TABLE chat.embedding_vectors;
        DROP TABLE chat.embedding_jobs;
        """);
}
