using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alycia.Modules.Chat.Infrastructure.Migrations;

[DbContext(typeof(ChatDbContext))]
[Migration("20261120100000_KnowledgeIndex")]
public sealed class KnowledgeIndexMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE chat.embedding_jobs ADD COLUMN retry_at timestamptz, ADD COLUMN cycle_calls integer NOT NULL DEFAULT 0,
            ADD COLUMN vector_digest text, ADD COLUMN vector_dimensions integer;
        CREATE TABLE chat.knowledge_units (
            key text PRIMARY KEY, cache_key text NOT NULL, file_id uuid REFERENCES chat.attachments("Id") ON DELETE CASCADE,
            conversation_id uuid REFERENCES chat.conversations("Id") ON DELETE CASCADE,
            profile text NOT NULL, source_hash text NOT NULL, level text NOT NULL,
            first_unit integer NOT NULL, last_unit integer NOT NULL, observed timestamptz,
            payload jsonb NOT NULL, indexed_at timestamptz NOT NULL DEFAULT now(),
            search_terms tsvector GENERATED ALWAYS AS (to_tsvector('simple'::regconfig,
                coalesce(payload#>>'{labels,summary}','') || ' ' || coalesce(payload#>>'{labels,keywords}','') || ' ' ||
                coalesce(payload#>>'{labels,topics}','') || ' ' || coalesce(payload#>>'{labels,entities}','') || ' ' ||
                coalesce(payload#>>'{labels,technicalSymbols}',''))) STORED,
            CHECK (num_nonnulls(file_id, conversation_id) = 1));
        CREATE INDEX ix_knowledge_cache ON chat.knowledge_units(cache_key, indexed_at DESC);
        CREATE INDEX ix_knowledge_files ON chat.knowledge_units(file_id, profile, source_hash, level);
        CREATE INDEX ix_knowledge_history_terms ON chat.knowledge_units USING gin(search_terms) WHERE level='history-group';
        CREATE INDEX ix_knowledge_history ON chat.knowledge_units(conversation_id, profile, level, last_unit);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE chat.knowledge_units; ALTER TABLE chat.embedding_jobs DROP COLUMN retry_at, DROP COLUMN cycle_calls, DROP COLUMN vector_digest, DROP COLUMN vector_dimensions;");
}
