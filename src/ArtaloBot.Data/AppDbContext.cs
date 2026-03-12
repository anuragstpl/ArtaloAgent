using ArtaloBot.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ArtaloBot.Data;

public class AppDbContext : DbContext
{
    public DbSet<ChatSession> ChatSessions { get; set; } = null!;
    public DbSet<ChatMessage> ChatMessages { get; set; } = null!;
    public DbSet<AppSetting> AppSettings { get; set; } = null!;
    public DbSet<MemoryEntry> MemoryEntries { get; set; } = null!;
    public DbSet<MCPServerConfig> MCPServers { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.Model).HasMaxLength(100);
            entity.Property(e => e.SystemPrompt).HasMaxLength(10000);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.IsArchived);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Model).HasMaxLength(100);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.Session)
                .WithMany(s => s.Messages)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(255);
            entity.Property(e => e.Value).IsRequired();
        });

        modelBuilder.Entity<MemoryEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.EmbeddingJson).IsRequired();
            entity.Property(e => e.EmbeddingModel).HasMaxLength(100);
            entity.Property(e => e.Tag).HasMaxLength(100);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<MCPServerConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.ServerType).HasMaxLength(50);
            entity.Property(e => e.Command).HasMaxLength(500);
            entity.Property(e => e.Arguments).HasMaxLength(2000);
            entity.Property(e => e.WorkingDirectory).HasMaxLength(500);
            entity.Property(e => e.EnvironmentVariables).HasMaxLength(5000);
            entity.Property(e => e.Url).HasMaxLength(500);
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.IsEnabled);
        });
    }
}

public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsEncrypted { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
