using CopilotDeepSeek.Database;
using CopilotDeepSeek.Database.Constants;
using CopilotDeepSeek.Models;
using CopilotDeepSeek.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace CopilotDeepSeek;

/// <summary>
/// Central application bootstrapper — sets up DI, database, and configuration.
/// </summary>
internal static class Bootstrap
{
    /// <summary>
    /// Initializes the application: parses args, builds DI, runs migrations, loads settings.
    /// </summary>
    public static BootstrapResult Initialize(string[] args)
    {
        // 1. Parse command-line arguments
        ParseArgs(args, out bool hidden, out bool reset, out VerbosityLevel verbosity);

        // 2. Build DI container (lean — no Host required)
        var services = new ServiceCollection();
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        try
        {
            // 3. Run migrations
            using (var scope = provider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                dbContext.Database.Migrate();

                dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            }

            // 4. Resolve services
            var settingsService = provider.GetRequiredService<ISettingsService>();

            // 5. Handle reset
            if (reset)
                settingsService.Reset();

            // 6. Load settings
            var settings = settingsService.LoadOrCreate();

            return new BootstrapResult(
                SettingsService: settingsService,
                ServiceProvider: provider,
                Settings: settings,
                Verbosity: verbosity,
                Hidden: hidden
            );
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Database
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={DbConstants.DatabaseFileName}"));

        // Application services
        services.AddSingleton<ISettingsService, SettingsService>();
    }

    private static void ParseArgs(
        string[] args,
        out bool hidden,
        out bool reset,
        out VerbosityLevel verbosity)
    {
        hidden = args.Contains("--hidden", StringComparer.OrdinalIgnoreCase);
        reset = args.Contains("--reset", StringComparer.OrdinalIgnoreCase);

        verbosity = VerbosityLevel.None;

        for (int i = 0; i < args.Length; i++)
        {
            ReadOnlySpan<char> arg = args[i];

            if (arg.StartsWith("--verbosity=", StringComparison.OrdinalIgnoreCase))
            {
                var valueSpan = arg["--verbosity=".Length..];
                if (int.TryParse(valueSpan, out int v) && Enum.IsDefined(typeof(VerbosityLevel), v))
                    verbosity = (VerbosityLevel)v;
            }
            else if (arg.Equals("--verbosity", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int v) && Enum.IsDefined(typeof(VerbosityLevel), v))
                    verbosity = (VerbosityLevel)v;
            }
        }

        // Default to All if no verbosity specified
        verbosity = verbosity == VerbosityLevel.None ? VerbosityLevel.All : verbosity;
    }
}