using CopilotDeepSeek.Database.Constants;
using CopilotDeepSeek.Database.Entities;
using CopilotDeepSeek.Database.ValueGenerators;
using Microsoft.EntityFrameworkCore;

namespace CopilotDeepSeek.Database;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ProxyRequest> ProxyRequests => Set<ProxyRequest>();
    public DbSet<AiModel> AiModels => Set<AiModel>();
    public DbSet<SettingsEntity> Settings => Set<SettingsEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseSqlite($"Data Source={DbConstants.DatabaseFileName}", o => o.CommandTimeout(30));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProxyRequest>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasValueGenerator<UuidV7Generator>()
                .ValueGeneratedOnAdd();

            entity.HasIndex(e => e.Timestamp);

            entity.HasIndex(e => e.IsSuccess);

            entity.Property(e => e.Timestamp)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<AiModel>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(128);

            entity.HasIndex(e => e.Name).IsUnique();

            entity.Property(e => e.IsAllowed)
                .HasDefaultValue(true);
        });

        modelBuilder.Entity<SettingsEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ApiKey)
                .HasMaxLength(1024);

            entity.Property(e => e.BaseUrl)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(e => e.AutoRun)
                .HasDefaultValue(true);

            entity.Property(e => e.Port)
                .HasDefaultValue(5000);

            entity.Property(e => e.MaxMessages)
                .HasDefaultValue(0);

            entity.Property(e => e.BalanceRefreshIntervalSec)
                .HasDefaultValue(60);

            // Ensure only one row can exist by using a fixed Id
            entity.HasData(new SettingsEntity { Id = 1 });
        });
    }
}