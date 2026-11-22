using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alycia.Modules.Chat.Infrastructure.Migrations;

[DbContext(typeof(ChatDbContext))]
[Migration("20261120010000_TemporalMemory")]
public sealed class TemporalMemoryProjection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE chat.temporal_memory (
            owner uuid NOT NULL REFERENCES chat.conversations("Id") ON DELETE CASCADE,
            sequence integer NOT NULL, observed timestamptz NOT NULL, topics text[] NOT NULL,
            facts jsonb NOT NULL, search tsvector NOT NULL, version smallint NOT NULL,
            PRIMARY KEY(owner,sequence));
        CREATE INDEX ix_temporal_topics ON chat.temporal_memory USING gin(topics);
        CREATE INDEX ix_temporal_search ON chat.temporal_memory USING gin(search);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE chat.temporal_memory");
}
