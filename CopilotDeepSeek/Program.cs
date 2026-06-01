namespace CopilotDeepSeek;

using CopilotDeepSeek.Models;
using System.Text.Json;

class Program
{
    private static bool _appShouldRun = true;
    private static ProxyServer? _proxy;

    private const string configPath = "settings.json";

    static void Main(string[] args)
    {

        // Create settings.json with defaults if missing
        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, JsonSerializer.Serialize(new Settings(), AppJsonContext.Default.Settings));
        }

        // Read settings
        var json = File.ReadAllText(configPath);
        var settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.Settings)!;

        // Prompt for API key if empty
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            Console.WriteLine("No API key found.");
            Console.WriteLine("Paste your DeepSeek API key below (text will be hidden) and press Enter:");
            string apiKey = SecurityHelper.ReadInput();

            settings.ApiKey = SecurityHelper.Encrypt(apiKey);
            string updatedJson = JsonSerializer.Serialize(settings, AppJsonContext.Default.Settings);
            File.WriteAllText(configPath, updatedJson);

            Console.WriteLine("API key saved securely.");
        }

        // Start proxy on background thread if AutoRun is enabled
        if (settings.AutoRun)
        {
            _proxy = new ProxyServer(settings);
            _proxy.Start();
        }

        Helper.PrintTitle();
        Helper.PrintBanner(_proxy?.IsRunning == true);

        while (_appShouldRun)
        {
            switch (Console.ReadKey(true))
            {
                // Exit
                case var key when key.Key == ConsoleKey.E:
                    Console.WriteLine("Shutting down...");
                    _proxy?.Stop();
                    Thread.Sleep(500);
                    _appShouldRun = false;
                    break;

                // Settings
                case var key when key.Key == ConsoleKey.S:
                    PrintSettings(settings, configPath);
                    break;

                // Toggle proxy (start/stop manually)
                case var key when key.Key == ConsoleKey.P:
                    if (_proxy?.IsRunning == true)
                    {
                        _proxy.Stop();
                        Console.WriteLine("Proxy stopped.");
                    }
                    else
                    {
                        _proxy = new ProxyServer(settings);
                        _proxy.Start();
                        Console.WriteLine("Proxy started.");
                    }
                    break;

                default:
                    break;
            }
        }

        _proxy?.Dispose();
    }

    internal static void PrintSettings(Settings settings, string configPath)
    {
        Console.Clear();
        Console.WriteLine("=== Current Settings ===\n");

        Console.WriteLine($"  Base URL : {settings.BaseUrl}");
        Console.WriteLine($"  Model    : {settings.Model}");
        Console.WriteLine($"  Port     : {settings.Port}");
        Console.WriteLine($"  Auto Run : {settings.AutoRun}");
        Console.WriteLine($"  Proxy    : {(_proxy?.IsRunning == true ? "Running" : "Stopped")}");

        string apiKey = string.IsNullOrEmpty(settings.ApiKey)
            ? "(not set)"
            : SecurityHelper.Decrypt(settings.ApiKey);
        Console.WriteLine($"  API Key  : {apiKey}");

        Console.WriteLine($"\nConfig file: {Path.GetFullPath(configPath)}");
        Console.WriteLine("\nPress any key to return...");
        Console.ReadKey(true);
    }
}