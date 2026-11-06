using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alycia.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_identifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    owner_slot = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.CheckConstraint("ck_identity_users_id_nonempty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_identity_users_login_identifier", "login_identifier ~ '^[A-Z0-9._-]{3,64}$'");
                    table.CheckConstraint("ck_identity_users_owner_slot", "owner_slot = 1");
                    table.CheckConstraint("ck_identity_users_password_hash", "length(password_hash) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_identity_users_single_owner",
                schema: "identity",
                table: "users",
                column: "owner_slot",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");
        }
    }
}
