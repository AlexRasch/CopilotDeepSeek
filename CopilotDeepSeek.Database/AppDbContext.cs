using CopilotDeepSeek.Database.Constants;
using CopilotDeepSeek.Database.Entities;
using CopilotDeepSeek.Database.ValueGenerators;
using Microsoft.EntityFrameworkCore;

namespace CopilotDeepSeek.Database;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ProxyRequest> ProxyRequests => Set<ProxyRequest>();

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
    }
}