using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<DocumentAnalysis> DocumentAnalyses => Set<DocumentAnalysis>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<PromptProfile> PromptProfiles => Set<PromptProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("chat");
        modelBuilder.Entity<DocumentAnalysis>(entity =>
        {
            entity.ToTable("document_analyses");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).ValueGeneratedNever();
            entity.Property(a => a.Title).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Model).HasMaxLength(200).IsRequired();
            entity.Property(a => a.SourceFingerprint).HasMaxLength(64).IsRequired();
            entity.HasIndex(a => new { a.ConversationId, a.CreatedAtUtc });
            entity.HasOne<Conversation>().WithMany().HasForeignKey(a => a.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PromptProfile>(entity =>
        {
            entity.ToTable("prompt_profiles");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedNever();
            entity.Property(p => p.Name).HasMaxLength(100).IsRequired();
            entity.Property(p => p.SystemPrompt).HasMaxLength(4000).IsRequired();
        });
        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.ToTable("attachments");
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Id).ValueGeneratedNever();
            entity.Property(f => f.Name).HasMaxLength(1024).IsRequired();
            entity.Property(f => f.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(f => f.Status).HasMaxLength(100).IsRequired();
            entity.Property(f => f.ContextMode).HasMaxLength(16).HasDefaultValue("window").IsRequired();
            entity.Property(f => f.SourceCreatedAt).HasMaxLength(64).HasDefaultValue("").IsRequired();
            entity.Property(f => f.SourceModifiedAt).HasMaxLength(64).HasDefaultValue("").IsRequired();
            entity.Property(f => f.MetadataSource).HasMaxLength(100).HasDefaultValue("unknown").IsRequired();
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
            entity.Property(item => item.SettingsJson).HasDefaultValue("{}").IsRequired();
            entity.Property(item => item.ContextNotes).HasMaxLength(8000).HasDefaultValue("").IsRequired();
            entity.Property(item => item.MemoryText).HasDefaultValue("").IsRequired();
            entity.Property(item => item.MemorySourceHash).HasMaxLength(64).HasDefaultValue("").IsRequired();
            entity.Property(item => item.MemoryModel).HasMaxLength(200).HasDefaultValue("").IsRequired();
            entity.Property(item => item.Revision).IsConcurrencyToken();
            entity.HasIndex(item => item.UpdatedAtUtc);
        });
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(item => new { item.ConversationId, item.Sequence });
            entity.Property(item => item.Role).HasMaxLength(16).IsRequired();
            entity.Property(item => item.Content).IsRequired();
            entity.Property(item => item.ToolTraceJson).HasDefaultValue("[]").IsRequired();
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
