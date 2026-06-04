using CopilotDeepSeek.Database.Entities;
using CopilotDeepSeek.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotDeepSeek;

class Program
{
    private static bool _appShouldRun = true;
    private static ProxyServer? _proxy;
    private static BootstrapResult _ctx = null!;
    private static VerbosityLevel _verbosity => _ctx.Verbosity;

    static void Main(string[] args)
    {
        try
        {
            // All bootstrap in one call
            _ctx = Bootstrap.Initialize(args);

            // Start proxy if configured to auto-run
            StartProxyIfAutoRun();

            if (!_ctx.Hidden)
            {
                Helper.PrintTitle();
                Helper.PrintBanner(_proxy?.IsRunning == true);
                RunInputLoop();
            }
            else
            {
                Thread.Sleep(Timeout.Infinite);
            }
        }
        finally
        {
            _proxy?.Stop();
            _proxy?.Dispose();

            (_ctx?.ServiceProvider as IDisposable)?.Dispose();
        }
    }

    // API key

    private static bool ApikeyExist()
    {
        if (!string.IsNullOrEmpty(_ctx.Settings.ApiKey))
            return true;
        return false;
    }

    private static void EnsureApiKey()
    {
        if (ApikeyExist())
            return;

        Console.WriteLine("No API key found.");
        Console.WriteLine("Paste your DeepSeek API key below (text will be hidden) and press Enter:");
        string apiKey = SecurityHelper.ReadInput();

        _ctx.Settings.ApiKey = SecurityHelper.Encrypt(apiKey);
        _ctx.SettingsService.Save(_ctx.Settings);
        Console.WriteLine("API key saved securely.");
    }

    // Proxy management

    private static void StartProxyIfAutoRun()
    {
        if (!_ctx.Settings.AutoRun)
            return;

        if (_ctx.Hidden && !ApikeyExist())
        {
            Console.WriteLine("Missing API key.");
            Environment.Exit(0);
        }

        EnsureApiKey();

        _proxy = new ProxyServer(_ctx.Settings);

        if (!_ctx.Hidden && _verbosity != VerbosityLevel.None)
        {
            _proxy.RequestCompleted += OnRequestCompleted;
        }
        // ToDo add setting for loggning 
        _proxy.RequestCompleted += OnRequestCompletedToDatabase;


        if (!_proxy.Start() && !_ctx.Hidden)
        {
            Console.WriteLine($"Warning: Could not start proxy on port {_ctx.Settings.Port} — port is already in use.");
        }
    }

    private static void ToggleProxy()
    {
        if (_proxy?.IsRunning == true)
        {
            _proxy.Stop();
            Console.WriteLine("Proxy stopped.");
        }
        else
        {
            EnsureApiKey();

            _proxy = new ProxyServer(_ctx.Settings);

            if (_verbosity != VerbosityLevel.None)
            {
                _proxy.RequestCompleted += OnRequestCompleted;
                _proxy.RequestCompleted += OnRequestCompletedToDatabase;
            }

            if (_proxy.Start())
            {
                Console.WriteLine("Proxy started on port " + _ctx.Settings.Port + ".");
            }
            else
            {
                Console.WriteLine($"Failed to start proxy — port {_ctx.Settings.Port} is already in use.");
            }
        }
    }

    // Request callback

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

    private static void OnRequestCompletedToDatabase(ProxyRequestEvent evt)
    {
        _ = Task.Run(async () =>
        {
            try
            {

                using var scope = _ctx.ServiceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<CopilotDeepSeek.Database.AppDbContext>();

                dbContext.ProxyRequests.Add(new ProxyRequest
                {
                    Id = Guid.CreateVersion7(),
                    Method = evt.Method,
                    Path = evt.Path,
                    StatusCode = evt.StatusCode,
                    Elapsed = evt.Elapsed,
                    IsSuccess = evt.IsSuccess,
                    ErrorMessage = evt.ErrorMessage,
                    Timestamp = DateTime.UtcNow
                });

                dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to log request to database: {ex.Message}");
            }
        });
    }

    // Input loop

    private static void RunInputLoop()
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
                    Helper.PrintSettings(_ctx.Settings, _proxy?.IsRunning == true);
                    break;

                case var k when k.Key == ConsoleKey.P:
                    ToggleProxy();
                    break;
            }
        }
    }

    private static void Shutdown()
    {
        Console.WriteLine("Shutting down...");
        Thread.Sleep(500);
        _appShouldRun = false;
    }
}