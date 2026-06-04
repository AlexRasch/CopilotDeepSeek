using Microsoft.EntityFrameworkCore;
using CopilotDeepSeek.Database.Entities;
using CopilotDeepSeek.Database.Constants;

namespace CopilotDeepSeek.Database;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ProxyRequest> ProxyRequests => Set<ProxyRequest>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseSqlite($"Data Source={DbConstants.DatabaseFileName}",o => o.CommandTimeout(30));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProxyRequest>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Index för att snabbt hitta requests baserat på tidsstämpel
            entity.HasIndex(e => e.Timestamp);

            // Index för att filtrera på success/failure
            entity.HasIndex(e => e.IsSuccess);

            // Konfigurera default värde för Timestamp
            entity.Property(e => e.Timestamp)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }

    /// <summary>
    /// Auto-generate UUID v7 for new entities if not already set.
    /// </summary>
    public override int SaveChanges()
    {
        GenerateUuidV7();
        return base.SaveChanges();
    }

    /// <summary>
    /// Auto-generate UUID v7 for new entities if not already set.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GenerateUuidV7();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void GenerateUuidV7()
    {
        var entries = ChangeTracker.Entries<ProxyRequest>()
            .Where(e => e.State == EntityState.Added && e.Entity.Id == Guid.Empty);

        foreach (var entry in entries)
        {
            entry.Entity.Id = Guid.CreateVersion7();
        }
    }
}