using System.Reflection;

namespace CopilotDeepSeek;

public static class Helper
{
    public static void PrintTitle()
    {
        Console.Title = $"Copilot DeepSeek - {GetVersion()}";
    }

    public static void PrintBanner(bool proxyRunning = false)
    {
        Console.WriteLine("====== Copilot DeepSeek ======");
        Console.WriteLine($" Date    : {GetCurrentDate()}");
        Console.WriteLine($" Version : {GetVersion()}");
        Console.WriteLine($" Proxy   : {(proxyRunning ? "Running" : "Stopped")}");
        Console.WriteLine($" Port    : {GetProxyPort()}");
        Console.WriteLine("==============================");
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  E  - Exit");
        Console.WriteLine("  S  - Show settings");
        Console.WriteLine("  P  - Toggle proxy");
    }

    static string GetCurrentDate() => DateTime.Now.ToString("MM/dd/yyyy");

    static string GetVersion() =>
        Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "1.0.0";

    static int GetProxyPort()
    {
        // Read port from settings.json if available
        try
        {
            var json = File.ReadAllText("settings.json");
            var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("Port", out var port) ? port.GetInt32() : 5000;
        }
        catch
        {
            return 5000;
        }
    }
}