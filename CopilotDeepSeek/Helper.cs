using CopilotDeepSeek.Models;
using System.Reflection;

namespace CopilotDeepSeek;

public static class Helper
{
    public static void PrintTitle()
    {
        Console.Title = $"Copilot DeepSeek - {GetVersion()}";
    }

    public static void PrintBanner(bool proxyRunning = false, Settings settings = null)
    {
        Console.WriteLine("====== Copilot DeepSeek ======");
        Console.WriteLine($" Date    : {GetCurrentDate()}");
        Console.WriteLine($" Version : {GetVersion()}");
        Console.WriteLine($" Proxy   : {(proxyRunning ? "Running" : "Stopped")}");
        Console.WriteLine($" Port    : {(settings != null ? settings.Port.ToString() : "")}");
        Console.WriteLine($" Portal  : http://localhost:{(settings != null ? settings.Port : "")}/web");
        Console.WriteLine("==============================");
    }

    internal static void PrintSettings(Settings settings, bool proxyRunning)
    {
        Console.Clear();
        Console.WriteLine("=== Current Settings ===\n");
        Console.WriteLine($"  Base URL          : {settings.BaseUrl}");
        Console.WriteLine($"  Model             : {settings.Model}");
        Console.WriteLine($"  Allowed Models    : {(settings.AllowedModels?.Count > 0 ? string.Join(", ", settings.AllowedModels) : "(none)")}");
        Console.WriteLine($"  Port              : {settings.Port}");
        Console.WriteLine($"  Auto Run          : {settings.AutoRun}");
        Console.WriteLine($"  Max Messages      : {(settings.MaxMessages == 0 ? "All" : settings.MaxMessages.ToString())}");
        Console.WriteLine($"  Balance Refresh   : {settings.BalanceRefreshIntervalSec}s");
        Console.WriteLine($"  Proxy             : {(proxyRunning ? "Running" : "Stopped")}");

        string apiKey = string.IsNullOrEmpty(settings.ApiKey)
            ? "(not set)"
            : SecurityHelper.Decrypt(settings.ApiKey);
        Console.WriteLine($"  API Key           : {apiKey}");

        Console.WriteLine($"\nSettings stored in database (copilotdeepseek.db)");
        Console.WriteLine("\nPress any key to return...");
        Console.ReadKey(true);
        Console.Clear();
    }

    internal static void PrintHelp()
    {
        Console.WriteLine("=== Commands ===");
        Console.WriteLine(" C  - Clear screen");
        Console.WriteLine(" E  - Exit");
        Console.WriteLine(" S  - Show settings");
        Console.WriteLine(" P  - Toggle proxy");
    }

    static string GetCurrentDate() => DateTime.Now.ToString("MM/dd/yyyy");

    static string GetVersion() =>
        Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "1.0.0";
}