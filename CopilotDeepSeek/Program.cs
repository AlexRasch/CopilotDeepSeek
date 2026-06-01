namespace CopilotDeepSeek;

using System.Runtime.InteropServices;
using CopilotDeepSeek.Models;
using CopilotDeepSeek.Services;

class Program
{
    private static bool _appShouldRun = true;
    private static ProxyServer? _proxy;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1859:Use concrete types when possible for improved performance",
    Justification = "Interface used intentionally to support testability.")]
    private static readonly ISettingsService _settingsService = new SettingsService();

    static void Main(string[] args)
    {
        ParseArgs(args, out bool hidden, out bool reset);

        if (reset)
            _settingsService.Reset();

        var settings = _settingsService.LoadOrCreate();
        EnsureApiKey(settings);
        StartProxyIfAutoRun(settings);

        if (!hidden)
        {
            Helper.PrintTitle();
            Helper.PrintBanner(_proxy?.IsRunning == true);
            RunInputLoop(settings);
        }
        else
        {
            // No UI — block until process is killed
            Thread.Sleep(Timeout.Infinite);
        }

        _proxy?.Dispose();
    }

    // -------------------------------------------------------------------------
    // Arg parsing
    // -------------------------------------------------------------------------

    private static void ParseArgs(string[] args, out bool hidden, out bool reset)
    {
        hidden = args.Contains("--hidden", StringComparer.OrdinalIgnoreCase);
        reset = args.Contains("--reset", StringComparer.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // API key
    // -------------------------------------------------------------------------

    private static void EnsureApiKey(Settings settings)
    {
        if (!string.IsNullOrEmpty(settings.ApiKey))
            return;

        Console.WriteLine("No API key found.");
        Console.WriteLine("Paste your DeepSeek API key below (text will be hidden) and press Enter:");
        string apiKey = SecurityHelper.ReadInput();

        settings.ApiKey = SecurityHelper.Encrypt(apiKey);
        _settingsService.Save(settings);
        Console.WriteLine("API key saved securely.");
    }

    // -------------------------------------------------------------------------
    // Proxy management
    // -------------------------------------------------------------------------

    private static void StartProxyIfAutoRun(Settings settings)
    {
        if (!settings.AutoRun)
            return;

        _proxy = new ProxyServer(settings);
        _proxy.Start();
    }

    private static void ToggleProxy(Settings settings)
    {
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
    }

    // -------------------------------------------------------------------------
    // Input loop
    // -------------------------------------------------------------------------

    private static void RunInputLoop(Settings settings)
    {
        while (_appShouldRun)
        {
            switch (Console.ReadKey(true))
            {
                case var k when k.Key == ConsoleKey.E:
                    Shutdown();
                    break;

                case var k when k.Key == ConsoleKey.S:
                    PrintSettings(settings);
                    break;

                case var k when k.Key == ConsoleKey.P:
                    ToggleProxy(settings);
                    break;

                default:
                    break;
            }
        }
    }

    private static void Shutdown()
    {
        Console.WriteLine("Shutting down...");
        _proxy?.Stop();
        Thread.Sleep(500);
        _appShouldRun = false;
    }

    // -------------------------------------------------------------------------
    // Settings display
    // -------------------------------------------------------------------------

    internal static void PrintSettings(Settings settings)
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

        Console.WriteLine($"\nConfig file: {Path.GetFullPath("settings.json")}");
        Console.WriteLine("\nPress any key to return...");
        Console.ReadKey(true);
    }
}