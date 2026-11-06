using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alycia.Modules.Workspaces.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "owner_user_id",
                schema: "workspaces",
                table: "workspaces",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_workspaces_owner_user_id_nonempty",
                schema: "workspaces",
                table: "workspaces",
                sql: "owner_user_id IS NULL OR owner_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workspaces_owner_user_id_nonempty",
                schema: "workspaces",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                schema: "workspaces",
                table: "workspaces");
        }
    }
}
