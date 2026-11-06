using Alycia.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OwnerBootstrapRecord> OwnerBootstrap => Set<OwnerBootstrapRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OwnerBootstrapRecord>(entity =>
        {
            entity.ToTable("owner_bootstrap", "identity", table =>
            {
                table.HasCheckConstraint("ck_identity_bootstrap_slot", "id = 1");
                table.HasCheckConstraint("ck_identity_bootstrap_hash", "octet_length(secret_hash) = 32");
                table.HasCheckConstraint("ck_identity_bootstrap_attempts", "incorrect_attempts BETWEEN 0 AND 5");
            });
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(entry => entry.SecretHash).HasColumnName("secret_hash")
                .HasColumnType("bytea").IsRequired();
            entity.Property(entry => entry.ExpiresAtUtc).HasColumnName("expires_at_utc")
                .HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(entry => entry.IncorrectAttempts).HasColumnName("incorrect_attempts")
                .IsRequired();
            entity.Property(entry => entry.ConsumedAtUtc).HasColumnName("consumed_at_utc")
                .HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users", "identity", table =>
            {
                table.HasCheckConstraint(
                    "ck_identity_users_id_nonempty",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint(
                    "ck_identity_users_owner_slot",
                    "owner_slot = 1");
                table.HasCheckConstraint(
                    "ck_identity_users_login_identifier",
                    "login_identifier ~ '^[A-Z0-9._-]{3,64}$'");
                table.HasCheckConstraint(
                    "ck_identity_users_password_hash",
                    "length(password_hash) > 0");
            });

            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            entity.Property(user => user.LoginIdentifier)
                .HasColumnName("login_identifier")
                .HasMaxLength(User.MaxLoginLength)
                .IsRequired();

            entity.Property(user => user.PasswordHash)
                .HasColumnName("password_hash")
                .IsRequired();

            // A single fixed slot with a unique index limits the entire
            // installation to one owner, including concurrent inserts.
            entity.Property<short>("OwnerSlot")
                .HasColumnName("owner_slot")
                .HasDefaultValue((short)1)
                .IsRequired();
            entity.HasIndex("OwnerSlot")
                .IsUnique()
                .HasDatabaseName("ux_identity_users_single_owner");
        });
    }
}
