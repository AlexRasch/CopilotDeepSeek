using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using CopilotDeepSeek.Database.Constants;

namespace CopilotDeepSeek.Database;

/// <summary>
/// Används av `dotnet ef migrations` vid design-tid för att skapa en instans av DbContext.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Sökväg till databasfilen – hamnar i samma mapp som lösningen
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), DbConstants.DatabaseFileName);
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new AppDbContext(optionsBuilder.Options);
    }
}