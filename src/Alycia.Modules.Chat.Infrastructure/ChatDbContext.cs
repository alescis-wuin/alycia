using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("chat");
        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.ToTable("attachments");
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Id).ValueGeneratedNever();
            entity.Property(f => f.Name).HasMaxLength(1024).IsRequired();
            entity.Property(f => f.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(f => f.Status).HasMaxLength(100).IsRequired();
            entity.HasIndex(f => new { f.ConversationId, f.BatchId });
            entity.HasOne<Conversation>().WithMany().HasForeignKey(f => f.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("conversations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedNever();
            entity.Property(item => item.Title).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Model).HasMaxLength(200).IsRequired();
            entity.Property(item => item.SystemPrompt).HasMaxLength(4000).IsRequired();
            entity.Property(item => item.Revision).IsConcurrencyToken();
            entity.HasIndex(item => item.UpdatedAtUtc);
        });
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(item => new { item.ConversationId, item.Sequence });
            entity.Property(item => item.Role).HasMaxLength(16).IsRequired();
            entity.Property(item => item.Content).IsRequired();
            entity.HasIndex(item => new { item.ConversationId, item.RequestId, item.Role }).IsUnique();
            entity.HasOne<Conversation>().WithMany().HasForeignKey(item => item.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public ChatDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_CONNECTION")
            ?? "Host=localhost;Database=alycia;Username=alycia";
        return new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql(connection, provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "chat"))
            .Options);
    }
}
