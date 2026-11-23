using Alycia.Modules.Workspaces.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Workspaces.Infrastructure;

public sealed class WorkspacesDbContext(DbContextOptions<WorkspacesDbContext> options)
    : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PostgreSQL citext compares names without case while retaining the display name.
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces", "workspaces", table =>
            {
                table.HasCheckConstraint(
                    "ck_workspaces_id_nonempty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_workspaces_name_trimmed",
                    "name = btrim(name)");
                table.HasCheckConstraint(
                    "ck_workspaces_name_length",
                    "char_length(name) BETWEEN 1 AND 100");
            });

            entity.HasKey(workspace => workspace.Id);
            entity.Property(workspace => workspace.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            entity.Property(workspace => workspace.Name)
                .HasColumnName("name")
                .HasColumnType("citext")
                .IsRequired()
                .HasMaxLength(Workspace.MaxNameLength);

            entity.HasIndex(workspace => workspace.Name)
                .IsUnique()
                .HasDatabaseName("ux_workspaces_name");

            entity.Property(workspace => workspace.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasColumnType("timestamp with time zone")
                .IsRequired();
        });
    }
}
