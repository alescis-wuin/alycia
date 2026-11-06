using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alycia.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerBootstrap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "owner_bootstrap",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    incorrect_attempts = table.Column<int>(type: "integer", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_owner_bootstrap", x => x.id);
                    table.CheckConstraint("ck_identity_bootstrap_attempts", "incorrect_attempts BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_identity_bootstrap_hash", "octet_length(secret_hash) = 32");
                    table.CheckConstraint("ck_identity_bootstrap_slot", "id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "owner_bootstrap",
                schema: "identity");
        }
    }
}
