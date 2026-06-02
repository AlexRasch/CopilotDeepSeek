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

    // Tracks the current verbosity level for request logging.
    private static VerbosityLevel _verbosity = VerbosityLevel.None;

    static void Main(string[] args)
    {
        ParseArgs(args, out bool hidden, out bool reset, out VerbosityLevel parsedVerbosity);
        _verbosity = parsedVerbosity;


        if (reset)
            _settingsService.Reset();

        var settings = _settingsService.LoadOrCreate();
        EnsureApiKey(settings);
        StartProxyIfAutoRun(settings, hidden);

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

    private static void ParseArgs(string[] args, out bool hidden, out bool reset, out VerbosityLevel verbosity)
    {
        hidden = args.Contains("--hidden", StringComparer.OrdinalIgnoreCase);
        reset = args.Contains("--reset", StringComparer.OrdinalIgnoreCase);

        verbosity = VerbosityLevel.None;

        for (int i = 0; i < args.Length; i++)
        {
            // Support both "--verbosity 1" and "--verbosity=1"
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
        // If not defined fallback to 2
        verbosity = verbosity == VerbosityLevel.None ? VerbosityLevel.All : verbosity;

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

    private static void StartProxyIfAutoRun(Settings settings, bool hidden)
    {
        if (!settings.AutoRun)
            return;

        _proxy = new ProxyServer(settings);

        if (!hidden && _verbosity != VerbosityLevel.None)
            _proxy.RequestCompleted += OnRequestCompleted;

        if (!_proxy.Start() && !hidden)
        {
            Console.WriteLine($"Warning: Could not start proxy on port {settings.Port} — port is already in use.");
        }
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
            if (_proxy.Start())
            {
                Console.WriteLine("Proxy started on port " + settings.Port + ".");
            }
            else
            {
                Console.WriteLine($"Failed to start proxy — port {settings.Port} is already in use.");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Request callback
    // -------------------------------------------------------------------------

    private static void OnRequestCompleted(ProxyRequestEvent evt)
    {
        if (_verbosity == VerbosityLevel.FailuresOnly && evt.IsSuccess)
            return;

        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var status = evt.IsSuccess ? "OK " : "ERR";
        var ms = evt.Elapsed.TotalMilliseconds;

        if (evt.IsSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[{timestamp}] {status} {evt.StatusCode}  {evt.Method,-6} {evt.Path}  ({ms:F0} ms)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"[{timestamp}] {status} {evt.StatusCode}  {evt.Method,-6} {evt.Path}  ({ms:F0} ms)");

            if (!string.IsNullOrEmpty(evt.ErrorMessage))
                Console.Write($"  — {evt.ErrorMessage}");

            Console.WriteLine();
        }

        Console.ResetColor();
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
                case var k when k.Key == ConsoleKey.C:
                    Console.Clear();
                    Helper.PrintBanner();
                    break;

                case var k when k.Key == ConsoleKey.E:
                    Shutdown();
                    break;

                case var k when k.Key == ConsoleKey.H:
                    Console.Clear();
                    Helper.PrintHelp();
                    break;

                case var k when k.Key == ConsoleKey.S:
                    Helper.PrintSettings(settings, _proxy?.IsRunning == true);
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
}